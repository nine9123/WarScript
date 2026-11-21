using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// An operator or <c>::</c> whose operands have statically known types
    /// that plan 5.3 rejects is a compile error with the VM's TypeError
    /// message (v2 plan T3.8, PROP-28). The per-rule goldens are
    /// <c>Tests/Golden~/language/static_operand_*.ws</c>; these check the
    /// position, an import, and what is not inferred.
    /// </summary>
    [TestFixture]
    public class StaticOperandTests
    {
        private static (WarScriptLanguage script, List<string> output) Script(
            string source, Dictionary<string, string> files = null)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("main", source,
                files == null ? null : path => files.TryGetValue(path, out var text) ? text : null,
                (s, msg) => output.Add(msg));
            script.LogUnhandledErrors = false;
            return (script, output);
        }

        [Test]
        public void TextSubtraction_IsReportedAtTheOperator()
        {
            var (script, output) = Script("print 1\nx = \"Sir Bob\" - \"Sir \"\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Cannot apply '-' to text and text", result.Diagnostics[0].Message);
            Assert.AreEqual(2, result.Diagnostics[0].Line);
            Assert.AreEqual(15, result.Diagnostics[0].Column, "at the '-'");
            Assert.AreEqual(ScriptErrorKind.CompileError, script.Run().Error.Kind);
            CollectionAssert.IsEmpty(output, "nothing ran");
        }

        [Test]
        public void AnErrorInAnImport_NamesTheImport()
        {
            var (script, _) = Script(
                "import \"lib\"\nprint 1\n",
                new Dictionary<string, string> { ["lib"] = "fun f []\n    return -true\nend\n" });

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Cannot apply '-' to logical", result.Diagnostics[0].Message);
            Assert.AreEqual("lib", result.Diagnostics[0].ScriptName);
            Assert.AreEqual(2, result.Diagnostics[0].Line);
        }

        [Test]
        public void AnOperatorResult_HasAKnownType()
        {
            // 'a - 1' is a number whatever 'a' is; text '+' anything is text.
            var (script, _) = Script("a = 3\nx = (a - 1) + {1}\n");
            Assert.AreEqual("Cannot apply '+' to number and array", script.Compile().Diagnostics[0].Message);

            (script, _) = Script("a = 3\nx = (\"n\" + a) - 1\n");
            Assert.AreEqual("Cannot apply '-' to text and number", script.Compile().Diagnostics[0].Message);
        }

        [Test]
        public void VariablesAndCalls_AreNotInferred()
        {
            // Both operands must be known: 'a' could hold anything here, and
            // the VM's message names its runtime type.
            var (script, output) = Script(
                "a = \"x\"\n" +
                "begin\n" +
                "    y = a - 1\n" +
                "rescue e\n" +
                "    print e\n" +
                "end\n");

            var result = script.Compile();

            Assert.IsTrue(result.Success);
            CollectionAssert.IsEmpty(result.Diagnostics);
            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "Cannot apply '-' to text and number" }, output);
        }

        [Test]
        public void AcceptedOperandTypes_Compile()
        {
            var (script, output) = Script(
                "n = 2\n" +
                "print \"{n}\" + 1\n" +
                "print \"ab\" * 2 + \"!\"\n" +
                "print {1} + {2} << 3\n" +
                "print 1 == \"1\"\n" +
                "print \"a\" < \"b\"\n");

            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "21", "abab!", "[1, 2, 3]", "false", "true" }, output);
        }

        [Test]
        public void AnImportReadingItsImportersConstant_IsCheckedAgainstItsValue()
        {
            // The import is compiled before the importer is parsed: the
            // check waits for the whole module set.
            var (script, _) = Script(
                "import \"lib\"\nconst LABEL = \"x\"\n",
                new Dictionary<string, string> { ["lib"] = "fun f []\n    return LABEL * {1}\nend\n" });

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.Diagnostics.Count);
            Assert.AreEqual("Cannot apply '*' to text and array", result.Diagnostics[0].Message);
            Assert.AreEqual("lib", result.Diagnostics[0].ScriptName);
            Assert.AreEqual(2, result.Diagnostics[0].Line);
            Assert.AreEqual(18, result.Diagnostics[0].Column, "at the '*'");
        }

        [Test]
        public void AConstantOfAConstant_HasItsType_AndACycleHasNone()
        {
            var (script, _) = Script("const A = 2\nconst B = A * 3\nprint B < \"x\"\n");
            Assert.AreEqual("Cannot apply '<' to number and text", script.Compile().Diagnostics[0].Message);

            // Each read runs before the other's declaration: a NameError at runtime.
            (script, _) = Script("const A = B + 1\nconst B = A + 1\nprint A - \"x\"\n");
            Assert.IsTrue(script.Compile().Success);
        }

        [Test]
        public void AConstantInAClassBody_OrANameTheInstanceHolds_IsNotTyped()
        {
            // A class body's bare read may find a field of any class part first.
            var (script, _) = Script(
                "const K = 1\n" +
                "class C\n" +
                "    y = K - \"x\"\n" +
                "end\n");
            Assert.IsTrue(script.Compile().Success, "class body");

            // A host global named like the constant is read until the declaration runs.
            (script, _) = Script("print K - \"x\"\nconst K = 1\n");
            script.GlobalMemoryScope.Set("K", WarValue.FromText("host"));
            Assert.IsTrue(script.Compile().Success, "a global the instance holds");
        }

        [Test]
        public void AnEnumMemberOfAnEnumNotAtTheTopLevel_IsNotTyped()
        {
            var (script, _) = Script(
                "class Holder\n" +
                "    enum Inner\n" +
                "        A\n" +
                "    end\n" +
                "end\n" +
                "fun f [Inner]\n" +
                "    return Inner :: A - \"x\"\n" +
                "end\n");
            Assert.IsTrue(script.Compile().Success);
        }

        [Test]
        public void ARangeLoopsBounds_AreCheckedAtTheDots_IncludingAConstant()
        {
            var (script, _) = Script("const N = \"3\"\nloop i in 0..N\n    print i\nend\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.Diagnostics.Count);
            Assert.AreEqual("Cannot apply '<' to number and text", result.Diagnostics[0].Message);
            Assert.AreEqual(2, result.Diagnostics[0].Line);
            Assert.AreEqual(12, result.Diagnostics[0].Column, "at the '..'");
        }

        [Test]
        public void ARangeLoopsRuntimeBoundError_IsAtTheDots()
        {
            var (script, _) = Script("fun same [v]\n    return v\nend\nloop i in 0..same [true]\n    print i\nend\n");

            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.TypeError, result.Error.Kind);
            Assert.AreEqual(4, result.Error.Line);
            Assert.AreEqual(12, result.Error.Column);
        }

        [Test]
        public void AnEnumsNameMethodReadAsAField_OrCalledWithAnotherCount_IsNotTyped()
        {
            // `E :: name` is a NameError at runtime, not a number.
            var (script, output) = Script(
                "enum Rank\n" +
                "    LOW\n" +
                "end\n" +
                "begin\n" +
                "    print Rank :: name < \"a\"\n" +
                "rescue e\n" +
                "    print e\n" +
                "end\n" +
                "begin\n" +
                "    print Rank :: name [] - 1\n" +
                "rescue e\n" +
                "    print e\n" +
                "end\n");

            Assert.IsTrue(script.Compile().Success);
            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(2, output.Count);
            StringAssert.Contains("'name' is not a field of class 'Rank'", output[0]);
            StringAssert.DoesNotContain("Cannot apply", output[1]);
        }
    }
}
