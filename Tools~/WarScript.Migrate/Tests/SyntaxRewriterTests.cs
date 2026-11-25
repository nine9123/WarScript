// Tests of the legacy-to-current syntax rewriter (T3.12, SyntaxRewriter.cs).
// Compiled into Headless~/Tests with the rest of the migration tool.

using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Exception;
using WarScript.Migrate;

namespace Tests
{
    [TestFixture]
    public class SyntaxRewriterTests
    {
        private static string Rewrite(string legacy) => SyntaxRewriter.Rewrite(legacy).Source;

        [TestCase("print add [3, 4]", "print add(3, 4)")]
        [TestCase("x = f []", "x = f()")]
        [TestCase("fun add [a, b = 1]\nend", "fun add(a, b = 1)\nend")]
        [TestCase("g = fun [x] return x end", "g = fun(x) return x end")]
        [TestCase("class Dog [name] : Animal [name], Pet [name]\nend", "class Dog(name) : Animal(name), Pet(name)\nend")]
        [TestCase("p = new Point [3, 4]", "p = new Point(3, 4)")]
        [TestCase("a = {1, {2, 3}, {}}", "a = [1, [2, 3], []]")]
        [TestCase("print a{0} + a {i + 1}", "print a[0] + a [i + 1]")]
        [TestCase("print p :: x + p::y", "print p.x + p.y")]
        [TestCase("print p :: items{0} :: m [1]", "print p.items[0].m(1)")]
        [TestCase("print E :: name [E :: A]", "print E.name(E.A)")]
        [TestCase("n = d :: new Part [1]", "n = d.new Part(1)")]
        [TestCase("create [team: \"Red\", hp: 1]", "create(team: \"Red\", hp: 1)")]
        [TestCase("print add [\n    1,\n    2\n]", "print add(\n    1,\n    2\n)")]
        [TestCase("yield until (x > 0)", "yield until (x > 0)")]
        [TestCase("yield wait (1 + 1) * 2", "yield wait (1 + 1) * 2")]
        public void RewritesEachForm(string legacy, string current)
        {
            Assert.AreEqual(current, Rewrite(legacy));
        }

        [TestCase("arr = [1, 2]\nprint arr[0]")]
        [TestCase("print f(arr[0])")]
        [TestCase("g = fun(x) return x end")]
        [TestCase("print x (y)")]
        [TestCase("s[0] = \"x\"")]
        [TestCase("grid[0][1] += 1")]
        public void RefusesTheCurrentSyntax(string source)
        {
            // Review of T3.12: a second run turned `[1, 2]` into `(1, 2)` and
            // `arr[0]` into `arr(0)`, and reported success.
            Assert.Throws<SyntaxException>(() => SyntaxRewriter.Rewrite(source));
        }

        [Test]
        public void RefusesItsOwnOutput()
        {
            var once = Rewrite("fun f [a, b]\n    return a + b\nend\narr = {1, {2, 3}}\nprint f [arr{0}, arr{1}{0}]\nprint \"v={arr{0}} {f [1, 2]}\"");
            Assert.Throws<SyntaxException>(() => SyntaxRewriter.Rewrite(once));
        }

        [Test]
        public void KeepsCommentsLayoutAndFields()
        {
            const string legacy =
                "# call foo [x] here: comments stay as written\n" +
                "class P [x]\n" +
                "    fun f []\n" +
                "        return @x + this :: x   # trailing { comment }\n" +
                "    end\n" +
                "end\r\n" +
                "\tq = {1}\r" +
                "print q{0}\n";
            const string current =
                "# call foo [x] here: comments stay as written\n" +
                "class P(x)\n" +
                "    fun f()\n" +
                "        return @x + this.x   # trailing { comment }\n" +
                "    end\n" +
                "end\r\n" +
                "\tq = [1]\r" +
                "print q[0]\n";
            Assert.AreEqual(current, Rewrite(legacy));
        }

        [Test]
        public void RewritesTheCodeInInterpolations_NotTheEscapes()
        {
            Assert.AreEqual("print \"\\{a\\} {arr[0]} {f(1)} {p.x}\"",
                Rewrite("print \"\\{a\\} {arr{0}} {f [1]} {p :: x}\""));
            Assert.AreEqual("print $\"n={len(a[0])}\"", Rewrite("print $\"n={len [a{0}]}\""));
        }

        [Test]
        public void LeavesRawLiterals_AndListsThem()
        {
            var result = SyntaxRewriter.Rewrite("x = f [1]\ns = \"\"\"npc_say [\"hi\"]\"\"\"\n");
            Assert.AreEqual("x = f(1)\ns = \"\"\"npc_say [\"hi\"]\"\"\"\n", result.Source);
            CollectionAssert.AreEqual(new[] { 2 }, result.RawLiteralLines);
        }

        [Test]
        public void UnchangedSource_IsReturnedAsIs()
        {
            var result = SyntaxRewriter.Rewrite("x = 1 + 2\nprint x\n");
            Assert.AreEqual(0, result.Edits);
            Assert.AreEqual("x = 1 + 2\nprint x\n", result.Source);
        }

        [Test]
        public void SourceThatDoesNotLex_Throws()
        {
            Assert.Throws<SyntaxException>(() => SyntaxRewriter.Rewrite("x = \"unterminated"));
        }

        [Test]
        public void TheRewrittenScript_RunsAsTheLegacyOneDid()
        {
            const string legacy =
                "class Unit [hp]\n" +
                "    loot = {{1, 2}, {3, 4}}\n" +
                "    fun hit [d]\n" +
                "        @hp = @hp - d\n" +
                "        return this :: hp\n" +
                "    end\n" +
                "end\n" +
                "u = new Unit [10]\n" +
                "row = u :: loot{1}\n" +
                "print u :: hit [3] + row{0}\n" +
                "print \"{u :: loot}\"\n";
            var (_, output) = TestHelper.Run("rewritten", Rewrite(legacy));
            CollectionAssert.AreEqual(new List<string> { "10", "[[1, 2], [3, 4]]" }, output);
        }
    }
}
