using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WarScript;
using WarScript.Bytecode;
using WarScript.Exception;
using WarScript.Expression.Value;
using WarScript.Parser;

namespace Tests
{
    /// <summary>
    /// The compiler's limit checks (v2 plan T1.8, DX-2): what the bytecode
    /// format cannot encode is a <see cref="CompileError"/>, never a
    /// truncated operand. For each limit, generated source exactly at it
    /// and just past it.
    /// </summary>
    [TestFixture]
    public class CompilerLimitTests
    {
        // The generated sources are large; keep them out of the lexer cache.
        [TearDown]
        public void ClearLexerCache() => LexicalParser.ClearCache();

        private static WarScriptLanguage Script(string source) => new WarScriptLanguage("limits", source, null, null);

        private static CompiledFunction Compile(string source) => Script(source).CompileWithoutRunning().script;

        private static CompileError CompileFails(string source)
        {
            var script = Script(source);
            return Assert.Throws<CompileError>(() => script.CompileWithoutRunning());
        }

        private static string Repeat(int count, Func<int, string> item, string separator) =>
            string.Join(separator, Enumerable.Range(0, count).Select(item));

        // ── Function size: code, jumps, loops, handler addresses ─────────

        /// <summary>
        /// <paramref name="source"/>(k) grows by the same number of bytes per
        /// k. The largest k whose code fits in 65,535 bytes compiles; k + 1
        /// raises "function too large".
        /// </summary>
        private static void AssertSizeLimit(Func<int, string> source, Func<CompiledFunction, Chunk> chunkOf)
        {
            int one = chunkOf(Compile(source(1))).Count;
            int perItem = chunkOf(Compile(source(2))).Count - one;
            Assert.Greater(perItem, 0);
            int fits = 1 + (BytecodeVerifier.MaxCodeLength - one) / perItem;

            var atLimit = chunkOf(Compile(source(fits)));
            Assert.LessOrEqual(atLimit.Count, BytecodeVerifier.MaxCodeLength);
            Assert.Greater(atLimit.Count + perItem, BytecodeVerifier.MaxCodeLength);

            var error = CompileFails(source(fits + 1));
            Assert.AreEqual("function too large (bytecode exceeds 65,535 bytes)", error.Reason);
            Assert.AreEqual("limits", error.ScriptName);
            Assert.Greater(error.Line, 0);
        }

        private static Chunk Body(CompiledFunction script) => script.Chunk;

        [Test]
        public void StraightLineCode() =>
            AssertSizeLimit(k => Repeat(k, i => "x = 1", "\n"), Body);

        [Test]
        public void ForwardJump_OverAnIfBody() =>
            AssertSizeLimit(k => "if x == null\n" + Repeat(k, i => "x = 1", "\n") + "\nend", Body);

        [Test]
        public void BackwardJump_OfALoop() =>
            AssertSizeLimit(k => "loop i in 0..2\n" + Repeat(k, i => "x = 1", "\n") + "\nend", Body);

        [Test]
        public void HandlerAddresses_OfABeginBlock() =>
            AssertSizeLimit(k => "begin\n" + Repeat(k, i => "x = 1", "\n") + "\nrescue e\n    print e\nend", Body);

        [Test]
        public void FunctionBody()
        {
            var script = Script("fun f []\n" + Repeat(30000, i => "    x = 1", "\n") + "\nend");
            var error = Assert.Throws<CompileError>(() => script.CompileWithoutRunning());
            Assert.AreEqual("function too large (bytecode exceeds 65,535 bytes)", error.Reason);
        }

        [Test]
        public void TheErrorNamesTheStatementThatCrossedTheLimit()
        {
            // The jump of the 'if' on line 2 is the first thing past 16 bits.
            var error = CompileFails("x = 0\nif x == null\n" + Repeat(30000, i => "x = 1", "\n") + "\nend\nprint x");
            Assert.AreEqual(2, error.Line);
            StringAssert.Contains("at line 2 in 'limits'", error.Message);
        }

        [Test]
        public void ACompileErrorLeavesRunLikeASyntaxError()
        {
            var script = Script(Repeat(30000, i => "x = 1", "\n"));
            var result = script.Run();
            Assert.AreEqual(ScriptErrorKind.CompileError, result.Error.Kind);
            Assert.IsInstanceOf<CompileError>(result.Error.CompileException);
            Assert.IsInstanceOf<SyntaxException>(new CompileError("r", null, 0));
        }

        // ── Arguments and parameters ─────────────────────────────────────

        private static string Args(int count) => Repeat(count, i => "1", ", ");

        [Test]
        public void ArgumentCount_ExactlyAtTheLimitRuns()
        {
            var source = "fun f [" + Repeat(255, i => "p" + i, ", ") + "]\n    return p0 + p254\nend\nprint f [" + Args(255) + "]";
            var (_, output) = TestHelper.Run("limits", source);
            CollectionAssert.AreEqual(new[] { "2" }, output);
        }

        [TestCase("print f [{0}]")]                                                      // Call
        [TestCase("fun g []\n    return f [{0}]\nend")]                                    // TailCall
        [TestCase("fun g [h]\n    return h [{0}]\nend")]                                   // CallValue
        [TestCase("class C\n    fun m []\n    end\nend\nc = new C\nprint c :: m [{0}]")]   // CallMethod
        [TestCase("class C\nend\nc = new C [{0}]")]                                         // NewInstance
        [TestCase("class C\n    class D\n    end\nend\nc = new C\nd = c :: new D [{0}]")]  // NewNestedInstance
        public void ArgumentCount_PastTheLimitIsACompileError(string template)
        {
            Compile(template.Replace("{0}", Args(255)));
            LexicalParser.ClearCache();
            var source = template.Replace("{0}", Args(256));
            var error = CompileFails(source);
            Assert.AreEqual("more than 255 arguments in a call", error.Reason);
            Assert.AreEqual(Array.FindIndex(template.Split('\n'), l => l.Contains("{0}")) + 1, error.Line);
        }

        [Test]
        public void ParameterCount()
        {
            Compile("fun f [" + Repeat(255, i => "p" + i, ", ") + "]\nend");
            Compile("g = fun [" + Repeat(255, i => "p" + i, ", ") + "] end");
            var function = CompileFails("x = 1\n  fun f [" + Repeat(256, i => "p" + i, ", ") + "]\nend");
            Assert.AreEqual("more than 255 parameters", function.Reason);
            Assert.AreEqual(2, function.Line);
            Assert.AreEqual(3, function.Column, "at the function's 'fun' (T3.1)");
            var lambda = CompileFails("x = 1\ng = fun [" + Repeat(256, i => "p" + i, ", ") + "] end");
            Assert.AreEqual("more than 255 parameters", lambda.Reason);
            Assert.AreEqual(2, lambda.Line);
            Assert.AreEqual(5, lambda.Column, "at the lambda's 'fun' (T3.1)");
        }

        // ── PopN ─────────────────────────────────────────────────────────

        // No statement pops more than a few stack locals today (only loops
        // and rescue variables are stack locals, a few per scope), so the
        // split is checked on the chunk.
        [TestCase(1, new byte[] { (byte)OpCode.Pop })]
        [TestCase(2, new byte[] { (byte)OpCode.PopN, 2 })]
        [TestCase(255, new byte[] { (byte)OpCode.PopN, 255 })]
        [TestCase(256, new byte[] { (byte)OpCode.PopN, 255, (byte)OpCode.PopN, 1 })]
        [TestCase(600, new byte[] { (byte)OpCode.PopN, 255, (byte)OpCode.PopN, 255, (byte)OpCode.PopN, 90 })]
        public void PopN_IsSplitAbove255(int count, byte[] expected)
        {
            var chunk = new Chunk();
            chunk.EmitPops(count, 1);
            CollectionAssert.AreEqual(expected, chunk.Code);
        }

        // ── Array literals ───────────────────────────────────────────────

        [Test]
        public void ArrayLiteral()
        {
            // 65,535 elements fit the operand but not a function: the size
            // limit is what stops it. One more is the array literal error.
            Assert.AreEqual("function too large (bytecode exceeds 65,535 bytes)",
                CompileFails("x = {" + Repeat(65535, i => "null", ", ") + "}").Reason);
            LexicalParser.ClearCache();
            var error = CompileFails("x = 1\nx = {" + Repeat(65536, i => "null", ", ") + "}");
            Assert.AreEqual("array literal too large; build it with <<", error.Reason);
            Assert.AreEqual(2, error.Line);
        }

        // ── Chunk: constants and U16 operands ────────────────────────────

        [Test]
        public void ConstantPool()
        {
            var chunk = new Chunk();
            for (int i = 0; i < Chunk.MaxConstants; i++)
                Assert.AreEqual(i, chunk.AddConstant(WarValue.FromNumeric(i)));
            // An existing constant is still found; a new one does not fit.
            Assert.AreEqual(7, chunk.AddConstant(WarValue.FromNumeric(7)));
            var error = Assert.Throws<CompileError>(() => chunk.AddConstant(WarValue.FromNumeric(-1)));
            Assert.AreEqual("too many constants in one function (more than 65,535)", error.Reason);
            Assert.Throws<CompileError>(() => chunk.AddConstant(WarValue.FromText("new")));
            Assert.Throws<CompileError>(() => chunk.AddConstant(WarValue.True));
            Assert.AreEqual(Chunk.MaxConstants, chunk.Constants.Count);
        }

        [Test]
        public void U16Operand()
        {
            var chunk = new Chunk();
            chunk.EmitU16(0xFFFF, 1);
            Assert.AreEqual(new byte[] { 0xFF, 0xFF }, chunk.Code.ToArray());
            Assert.Throws<CompileError>(() => chunk.EmitU16(0x10000, 1));
            Assert.Throws<CompileError>(() => chunk.EmitU16(-1, 1));
            Assert.AreEqual(2, chunk.Count);
        }
    }
}
