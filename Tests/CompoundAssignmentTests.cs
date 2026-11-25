using System.Collections.Generic;
using NUnit.Framework;
using WarScript;

namespace Tests
{
    /// <summary>
    /// A compound assignment (<c>+=</c> <c>-=</c> <c>*=</c> <c>/=</c>) whose
    /// target has a side effect in a part it evaluates twice is a compile
    /// error at the operator (v2 plan T3.13, PROP-32): the desugar
    /// <c>target = target op e</c> would run that code twice. The goldens are
    /// <c>errors/compound_target_call.ws</c> and
    /// <c>language/compound_assignment_targets.ws</c>.
    /// </summary>
    [TestFixture]
    public class CompoundAssignmentTests
    {
        // Defines every name the rejected lines use, so that the compound
        // target is the only problem of the file.
        private const string Preamble =
            "fun g()\n" +
            "    return [1]\n" +
            "end\n" +
            "fun next_i()\n" +
            "    return 0\n" +
            "end\n" +
            "fun make_unit()\n" +
            "    return new Unit(1)\n" +
            "end\n" +
            "fun pick()\n" +
            "    return 0\n" +
            "end\n" +
            "fun k()\n" +
            "    return 0\n" +
            "end\n" +
            "fun f()\n" +
            "    return 0\n" +
            "end\n" +
            "class Unit(hp)\n" +
            "    items = [[1]]\n" +
            "    v = 0\n" +
            "    fun choose()\n" +
            "        return this\n" +
            "    end\n" +
            "end\n" +
            "class Box(v)\n" +
            "end\n" +
            "data = [1]\n" +
            "obj = new Unit(1)\n" +
            "units = new Unit(1)\n" +
            "grid = [[1]]\n" +
            "a = [1]\n" +
            "i = 0\n";

        private static readonly int PreambleLines = Preamble.Split('\n').Length - 1;

        private static WarScriptLanguage Script(string source)
        {
            var script = new WarScriptLanguage("main", source, null, (s, msg) => { });
            script.LogUnhandledErrors = false;
            return script;
        }

        private static string Message(string op, string effect) =>
            "The target of '" + op + "' is evaluated twice and " + effect + ": put that value in a variable first";

        [TestCase("g()[0] += 1", "+=", "calls 'g'")]
        [TestCase("g()[0] -= 1", "-=", "calls 'g'")]
        [TestCase("g()[0] *= 2", "*=", "calls 'g'")]
        [TestCase("g()[0] /= 2", "/=", "calls 'g'")]
        [TestCase("data[next_i()] -= 5", "-=", "calls 'next_i'")]
        [TestCase("make_unit().hp -= 1", "-=", "calls 'make_unit'")]
        [TestCase("obj.items[pick()] *= 2", "*=", "calls 'pick'")]
        [TestCase("new Box(1).v += 1", "+=", "creates a 'Box'")]
        [TestCase("a[i += 1] += 10", "+=", "assigns")]
        [TestCase("a[i = 0] += 10", "+=", "assigns")]
        [TestCase("(a << 5)[0] += 1", "+=", "appends with '<<'")]
        [TestCase("units.choose().hp -= 1", "-=", "calls 'choose'")]
        [TestCase("grid[f()][0] += 1", "+=", "calls 'f'")]
        [TestCase("obj.items[0][k()] += 1", "+=", "calls 'k'")]
        [TestCase("a[g()[0] + k()] += 1", "+=", "calls 'g'")]
        [TestCase("a[[k()][0]] += 1", "+=", "calls 'k'")]
        [TestCase("print 1 + (g()[0] += 1)", "+=", "calls 'g'")]
        public void ASideEffectInTheTarget_IsACompileErrorAtTheOperator(string line, string op, string effect)
        {
            var script = Script(Preamble + line + "\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.Diagnostics.Count, "one error per compound assignment");
            Assert.AreEqual(Message(op, effect), result.Diagnostics[0].Message);
            Assert.AreEqual(PreambleLines + 1, result.Diagnostics[0].Line);
            Assert.AreEqual(line.LastIndexOf(op, System.StringComparison.Ordinal) + 1, result.Diagnostics[0].Column,
                "at the (outer) compound operator");
            Assert.AreEqual(ScriptErrorKind.CompileError, script.Run().Error.Kind);
        }

        [Test]
        public void ACallInTheTargetInAClassMethod_IsACompileError()
        {
            var script = Script(
                "fun k()\n" +
                "    return 0\n" +
                "end\n" +
                "class C()\n" +
                "    items = [1]\n" +
                "    fun bump()\n" +
                "        this.items[k()] += 1\n" +
                "    end\n" +
                "end\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(Message("+=", "calls 'k'"), result.Diagnostics[0].Message);
            Assert.AreEqual(7, result.Diagnostics[0].Line);
            Assert.AreEqual(25, result.Diagnostics[0].Column);
        }

        [Test]
        public void TwoBadCompoundAssignments_ReportTwoErrors()
        {
            var script = Script(Preamble + "g()[0] += 1\ndata[next_i()] -= 5\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(2, result.Diagnostics.Count);
            Assert.AreEqual(Message("+=", "calls 'g'"), result.Diagnostics[0].Message);
            Assert.AreEqual(PreambleLines + 1, result.Diagnostics[0].Line);
            Assert.AreEqual(Message("-=", "calls 'next_i'"), result.Diagnostics[1].Message);
            Assert.AreEqual(PreambleLines + 2, result.Diagnostics[1].Line);
        }

        [Test]
        public void AnUndefinedFunctionInTheTarget_IsReportedOnce()
        {
            var script = Script("a = [1]\nmissing()[0] += 1\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.Diagnostics.Count);
            Assert.AreEqual(Message("+=", "calls 'missing'"), result.Diagnostics[0].Message);
        }

        [TestCase("x = 1\nx += 1\n")]
        [TestCase("a[i] += 1\n")]
        [TestCase("grid[0][0] += 1\n")]
        [TestCase("obj.hp -= 5\n")]
        [TestCase("obj.items[0][0] += 1\n")]
        [TestCase("a[0] += g()[0]\n")]
        [TestCase("a[0] += k()\n")]
        [TestCase("print 1 + (a[0] += 1)\n")]
        [TestCase("row = g()\nrow[0] += 1\n")]
        [TestCase("h = fun(n) return n end\na[0] += h(1)\n")]
        public void ATargetWithoutASideEffect_Compiles(string lines)
        {
            var result = Script(Preamble + lines).Compile();

            Assert.IsTrue(result.Success, result.Diagnostics.Count > 0 ? result.Diagnostics[0].Message : "");
        }
    }
}
