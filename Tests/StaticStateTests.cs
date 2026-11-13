using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit.Framework;
using WarScript;
using WarScript.Token;

namespace Tests
{
    /// <summary>
    /// T1.9 (DX-1, DX-6, R1): the runtime holds no static mutable state
    /// except explicitly synchronized caches, and the lexer cache is bounded
    /// and safe to use from several threads.
    /// </summary>
    [TestFixture]
    public class StaticStateTests
    {
        /// <summary>
        /// Static fields allowed although they hold mutable state, as
        /// "<c>Type.Field</c>". Every entry says why it is safe.
        /// </summary>
        private static readonly Dictionary<string, string> AllowList = new Dictionary<string, string>
        {
            // The lexer cache: a TokenCache synchronizes every access with its
            // own lock, holds at most its capacity, and hands out read-only token lists.
            ["WarScript.LexicalParser.Cache"] = "synchronized, bounded cache",
            // Filled by the static initializer, only read afterwards.
            ["WarScript.Expression.Operator.Extensions.OperatorExtension.OperatorMap"] = "initialized once, read-only afterwards",
            // Filled by the static constructor ("0".."999"), only read afterwards.
            ["WarScript.StringInterner.IntegerStrings"] = "initialized once, read-only afterwards",
            // The table of standard libraries; the runtime only reads it.
            ["WarScriptLibraryRegistry.Libraries"] = "initialized once, read-only afterwards",
            // Private lookup tables (arrays for speed) filled by their
            // initializer and only read afterwards; none is handed out.
            ["WarScript.StatementParser.StatementLeadTypes"] = "private constant table",
            ["WarScript.StatementParser.IfBranchKeywords"] = "private constant table",
            ["WarScript.StatementParser.CommaValue"] = "private constant table",
            ["WarScript.Expression.Value.WarValue.Pow10"] = "private constant table",
            ["WarScript.Bytecode.BytecodeSerializer.Magic"] = "private constant table",
            ["WarScript.Bytecode.OpInfo.ByOpcode"] = "private index built from OpInfo.Table once",
            // [ThreadStatic]: each thread has its own pool, which holds only
            // readers whose stacks were cleared when they were returned, so
            // nothing in it belongs to an instance.
            ["WarScript.Expression.ExpressionReader._pool"] = "thread-static pool of cleared readers",
        };

        private static IEnumerable<Type> RuntimeTypes()
        {
            // The headless build compiles FixPointCS into the same assembly
            // (Unity references it as its own assembly); its types don't count.
            // WarScriptLibraryRegistry is in the global namespace, so this
            // excludes FixPointCS rather than selecting WarScript.*.
            return typeof(WarScriptLanguage).Assembly.GetTypes()
                .Where(t => !IsNamespace(t, "FixMath") && !IsNamespace(t, "FixPointCS"))
                .Where(t => !IsCompilerGenerated(t));
        }

        private static bool IsNamespace(Type type, string ns)
        {
            return type.Namespace != null
                   && (type.Namespace == ns || type.Namespace.StartsWith(ns + ".", StringComparison.Ordinal));
        }

        private static bool IsCompilerGenerated(Type type)
        {
            // Lambda caches (`<>c`), iterator and closure classes: their static
            // fields are delegate caches the compiler fills lazily.
            for (var t = type; t != null; t = t.DeclaringType)
                if (t.IsDefined(typeof(CompilerGeneratedAttribute), false)) return true;
            return false;
        }

        private static bool IsMutableCollection(Type type)
        {
            if (type.IsArray) return true;
            if (type.IsInterface) return false; // IReadOnlyList<T> and the like
            if (typeof(ICollection).IsAssignableFrom(type)) return true;
            return type.GetInterfaces().Any(i => i.IsGenericType
                                                 && i.GetGenericTypeDefinition() == typeof(ICollection<>)
                                                 && !type.Name.StartsWith("ReadOnly", StringComparison.Ordinal));
        }

        private static List<string> Violations()
        {
            var violations = new List<string>();
            foreach (var type in RuntimeTypes())
            {
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsLiteral) continue; // const
                    var name = type.FullName + "." + field.Name;
                    if (AllowList.ContainsKey(name)) continue;
                    if (!field.IsInitOnly)
                        violations.Add(name + ": static field is neither readonly nor const");
                    else if (IsMutableCollection(field.FieldType))
                        violations.Add(name + ": readonly static field of mutable collection type " + field.FieldType.Name);
                }
            }
            return violations;
        }

        [Test]
        public void Runtime_HasNoStaticMutableState()
        {
            var violations = Violations();
            Assert.IsEmpty(violations, "Static mutable state in the runtime (R1, T1.9):\n" + string.Join("\n", violations));
        }

        [Test]
        public void AllowList_NamesExistingFields()
        {
            // An entry for a field that is gone or renamed must be removed,
            // or it would silently allow a new field of that name.
            var fields = new HashSet<string>(RuntimeTypes().SelectMany(t =>
                t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Select(f => t.FullName + "." + f.Name)));
            foreach (var entry in AllowList.Keys)
                Assert.IsTrue(fields.Contains(entry), "Allow-list entry names no static field: " + entry);
        }

        // ── Lexer cache ─────────────────────────────────────────────────

        [Test]
        public void Lexer_ReturnsReadOnlyTokens()
        {
            var tokens = LexicalParser.Parse("x = 1 + 2");
            Assert.IsFalse(tokens is List<Token>, "the cached list must not be mutable by a caller");
            Assert.IsFalse(tokens is Token[], "the cached list must not be mutable by a caller");
            Assert.AreSame(tokens, LexicalParser.Parse("x = 1 + 2"), "a second parse of the same source is a cache hit");
        }

        [Test]
        public void TokenCache_EvictsTheLeastRecentlyUsedEntry()
        {
            var cache = new TokenCache(3);
            var a = LexicalParser.Parse("a = 1");
            var b = LexicalParser.Parse("b = 1");
            var c = LexicalParser.Parse("c = 1");
            var d = LexicalParser.Parse("d = 1");
            cache.Add("a", a);
            cache.Add("b", b);
            cache.Add("c", c);
            Assert.IsTrue(cache.TryGet("a", out _)); // a is now the most recently used
            cache.Add("d", d);                         // evicts b

            Assert.AreEqual(3, cache.Count);
            Assert.IsFalse(cache.TryGet("b", out _));
            Assert.IsTrue(cache.TryGet("a", out var gotA));
            Assert.AreSame(a, gotA);
            Assert.IsTrue(cache.TryGet("c", out _));
            Assert.IsTrue(cache.TryGet("d", out _));

            cache.Clear();
            Assert.AreEqual(0, cache.Count);
            Assert.IsFalse(cache.TryGet("a", out _));
        }

        [Test]
        public void TokenCache_AddingAnExistingKeyKeepsOneEntry()
        {
            var cache = new TokenCache(2);
            var first = LexicalParser.Parse("x = 1");
            var second = LexicalParser.Parse("x = 2");
            cache.Add("k", first);
            cache.Add("k", second);
            Assert.AreEqual(1, cache.Count);
            Assert.IsTrue(cache.TryGet("k", out var got));
            Assert.AreSame(first, got, "the entry already cached wins, so every caller sees the same list");
        }

        [Test]
        public void LexerCache_IsBounded()
        {
            LexicalParser.ClearCache();
            for (var i = 0; i < LexicalParser.CacheCapacity * 3; i++)
                LexicalParser.Parse("x = " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.AreEqual(LexicalParser.CacheCapacity, LexicalParser.CachedCount);
            LexicalParser.ClearCache();
            Assert.AreEqual(0, LexicalParser.CachedCount);
        }

        [Test]
        public void Lexer_IsSafeFromSeveralThreads()
        {
            // More distinct sources than the cache holds, so threads hit,
            // miss, insert and evict at the same time.
            const int threadCount = 8;
            const int iterations = 2000;
            var sourceCount = LexicalParser.CacheCapacity * 2;
            var sources = new string[sourceCount];
            var expected = new int[sourceCount];
            for (var i = 0; i < sourceCount; i++)
            {
                var n = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                sources[i] = "fun f" + n + " [a]\n    return a + " + n + "\nend\nprint f" + n + " [1]";
            }
            LexicalParser.ClearCache();
            for (var i = 0; i < sourceCount; i++)
                expected[i] = LexicalParser.Parse(sources[i]).Count;

            var failures = new List<string>();
            var threads = new Thread[threadCount];
            for (var t = 0; t < threadCount; t++)
            {
                var seed = t;
                threads[t] = new Thread(() =>
                {
                    try
                    {
                        var x = (uint)(seed * 2654435761u + 1);
                        for (var i = 0; i < iterations; i++)
                        {
                            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
                            var k = (int)(x % (uint)sourceCount);
                            var tokens = LexicalParser.Parse(sources[k]);
                            if (tokens.Count != expected[k])
                                lock (failures) failures.Add("wrong token count for source " + k);
                            if ((i & 63) == 0 && seed == 0) LexicalParser.ClearCache();
                        }
                    }
                    catch (System.Exception e)
                    {
                        lock (failures) failures.Add(e.ToString());
                    }
                });
            }
            foreach (var thread in threads) thread.Start();
            foreach (var thread in threads) thread.Join();

            Assert.IsEmpty(failures, string.Join("\n", failures.Take(5)));
            Assert.LessOrEqual(LexicalParser.CachedCount, LexicalParser.CacheCapacity);
        }
    }
}
