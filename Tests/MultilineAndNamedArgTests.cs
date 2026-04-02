using System.Collections.Generic;
using NUnit.Framework;
using WarScript;

namespace Tests
{
    /// <summary>
    /// Tests for multiline function calls and named arguments.
    ///
    /// Multiline calls: arguments inside [ ] can span multiple lines.
    /// Named args:      arguments can be supplied as  name: value  pairs in any order,
    ///                  desugared at parse time to the positional order declared in fun[].
    /// </summary>
    [TestFixture]
    public class MultilineAndNamedArgTests
    {
        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        private static List<string> Run(string source)
        {
            var (_, output) = TestHelper.Run("test", source);
            return output;
        }

        // ─────────────────────────────────────────────────────────────
        // Multiline calls
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void Multiline_TwoArgs_SpanTwoLines()
        {
            var src = @"
fun add[a, b]
  return a + b
end
result = add[
  10,
  20
]
print result";
            Assert.AreEqual(new[] { "30" }, Run(src));
        }

        [Test]
        public void Multiline_FiveArgs_SpanFiveLines()
        {
            var src = @"
fun sum5[a, b, c, d, e]
  return a + b + c + d + e
end
result = sum5[
  1,
  2,
  3,
  4,
  5
]
print result";
            Assert.AreEqual(new[] { "15" }, Run(src));
        }

        [Test]
        public void Multiline_TrailingComma_Accepted()
        {
            var src = @"
fun sum3[a, b, c]
  return a + b + c
end
result = sum3[
  10,
  20,
  30,
]
print result";
            Assert.AreEqual(new[] { "60" }, Run(src));
        }

        [Test]
        public void Multiline_SingleLine_StillWorks()
        {
            var src = @"
fun add[a, b]
  return a + b
end
print add[3, 4]";
            Assert.AreEqual(new[] { "7" }, Run(src));
        }

        [Test]
        public void Multiline_ExpressionArgs()
        {
            var src = @"
fun sum3[a, b, c]
  return a + b + c
end
x = 5
y = 3
result = sum3[
  x * 2,
  y + 1,
  10
]
print result";
            // 10 + 4 + 10 = 24
            Assert.AreEqual(new[] { "24" }, Run(src));
        }

        [Test]
        public void Multiline_NestedCallsAsArgs()
        {
            var src = @"
fun add[a, b]
  return a + b
end
result = add[
  add[1, 2],
  add[3, 4]
]
print result";
            Assert.AreEqual(new[] { "10" }, Run(src));
        }

        [Test]
        public void Multiline_StringArgs()
        {
            var src = @"
fun greet[name, greeting]
  return greeting + "" "" + name
end
msg = greet[
  ""World"",
  ""Hello""
]
print msg";
            Assert.AreEqual(new[] { "Hello World" }, Run(src));
        }

        [Test]
        public void Multiline_ClassInstantiation()
        {
            var src = @"
class Point[x, y]
end
p = new Point[
  10,
  20
]
print p::x
print p::y";
            Assert.AreEqual(new[] { "10", "20" }, Run(src));
        }

        [Test]
        public void Multiline_FileBasedScript()
        {
            var (_, output) = TestHelper.RunFile("test_multiline_calls.ws");
            Assert.AreEqual(new[]
            {
                "30",   // single-line add[10,20]
                "30",   // multiline add
                "6",    // sum3 multiline
                "15",   // sum5 multiline
                "60",   // sum3 trailing comma
                "24",   // expression args (5*2 + 3+1 + 10)
                "10",   // nested calls
                "Hello World" // string args
            }, output);
        }

        // ─────────────────────────────────────────────────────────────
        // Named arguments
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void Named_InDeclaredOrder_SameAsPositional()
        {
            var src = @"
fun add[a, b]
  return a + b
end
print add[a: 10, b: 20]";
            Assert.AreEqual(new[] { "30" }, Run(src));
        }

        [Test]
        public void Named_ReorderedArgs_CorrectResult()
        {
            var src = @"
fun sub[a, b]
  return a - b
end
# named in reverse order, result should still be a-b = 10-3 = 7
print sub[b: 3, a: 10]";
            Assert.AreEqual(new[] { "7" }, Run(src));
        }

        [Test]
        public void Named_FourArgs_AllOrders_Match()
        {
            var src = @"
fun describe[name, health, damage, speed]
  return name + "" hp="" + health + "" dmg="" + damage + "" spd="" + speed
end
r1 = describe[""Orc"", 100, 25, 3]
r2 = describe[name: ""Orc"", health: 100, damage: 25, speed: 3]
r3 = describe[speed: 3, damage: 25, health: 100, name: ""Orc""]
print r1
print r2
print r3";
            Assert.AreEqual(new[] {
                "Orc hp=100 dmg=25 spd=3",
                "Orc hp=100 dmg=25 spd=3",
                "Orc hp=100 dmg=25 spd=3",
            }, Run(src));
        }

        [Test]
        public void Named_Multiline_WithTrailingComma()
        {
            var src = @"
fun spawn[type, x, y]
  return ""spawned "" + type + "" at "" + x + "","" + y
end
r = spawn[
  type: ""Archer"",
  x: 5,
  y: 10,
]
print r";
            Assert.AreEqual(new[] { "spawned Archer at 5,10" }, Run(src));
        }

        [Test]
        public void Named_OrderMatters_ArithmeticAsymmetric()
        {
            // calc[a, b, c] = a - b * c
            // All three call styles must give the same 4
            var src = @"
fun calc[a, b, c]
  return a - b * c
end
r1 = calc[10, 2, 3]
r2 = calc[a: 10, b: 2, c: 3]
r3 = calc[c: 3, a: 10, b: 2]
print r1
print r2
print r3";
            Assert.AreEqual(new[] { "4", "4", "4" }, Run(src));
        }

        [Test]
        public void Named_WithExpressionValues()
        {
            var src = @"
fun add[a, b]
  return a + b
end
base = 10
result = add[b: base * 2, a: base + 1]
print result";
            // a = 11, b = 20 → 31
            Assert.AreEqual(new[] { "31" }, Run(src));
        }

        [Test]
        public void Named_FileBasedScript()
        {
            var (_, output) = TestHelper.RunFile("test_named_args.ws");
            // r1 positional, r2 named-in-order, r3 named-reordered, r4 multiline,
            // r5 multiline-reordered, r6 with expressions, r7/r8/r9 asymmetric
            Assert.AreEqual(new[]
            {
                "Orc hp=100 dmg=25 spd=3",
                "Orc hp=100 dmg=25 spd=3",
                "Orc hp=100 dmg=25 spd=3",
                "Goblin hp=40 dmg=10 spd=6",
                "Goblin hp=40 dmg=10 spd=6",
                "spawned Archer at 20,15",
                "4",
                "4",
                "4",
            }, output);
        }

        // ─────────────────────────────────────────────────────────────
        // Edge cases
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void Multiline_NoArgs_EmptyBrackets()
        {
            var src = @"
fun hello[]
  return ""hi""
end
print hello[]";
            Assert.AreEqual(new[] { "hi" }, Run(src));
        }

        [Test]
        public void Multiline_CommentOnArgLine_Ignored()
        {
            var src = @"
fun add[a, b]
  return a + b
end
result = add[
  10, # first arg
  20  # second arg
]
print result";
            Assert.AreEqual(new[] { "30" }, Run(src));
        }

        [Test]
        public void Named_SingleArg()
        {
            var src = @"
fun double[x]
  return x * 2
end
print double[x: 7]";
            Assert.AreEqual(new[] { "14" }, Run(src));
        }

        [Test]
        public void Multiline_InsideCondition()
        {
            var src = @"
fun add[a, b]
  return a + b
end
if add[
  3,
  4
] == 7
  print ""yes""
end";
            Assert.AreEqual(new[] { "yes" }, Run(src));
        }

        [Test]
        public void Multiline_InsideLoop()
        {
            var src = @"
fun inc[x]
  return x + 1
end
total = 0
loop i in 1..4
  total = total + inc[
    i
  ]
end
print total";
            // inc[1]+inc[2]+inc[3] = 2+3+4 = 9  (range 1..4 is exclusive upper: 1,2,3)
            Assert.AreEqual(new[] { "9" }, Run(src));
        }
    }
}
