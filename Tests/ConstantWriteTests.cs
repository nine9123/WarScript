using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// A write of a constant's name anywhere in the module set is a compile
    /// error, <c>Cannot reassign constant 'X'</c>, at the write (v2 plan
    /// T3.8): the parser rejects the writes it reads after the declaration,
    /// the compiler the others — in an import of the declaring file, or above
    /// the declaration. The same-file goldens are
    /// <c>language/const_written_before_declared.ws</c>,
    /// <c>enum_written_by_function_above.ws</c> and
    /// <c>const_read_before_declared.ws</c>; these need an import or a host
    /// constant.
    /// </summary>
    [TestFixture]
    public class ConstantWriteTests
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
        public void AnImportWritingTheImportersConstant_IsACompileErrorInTheImport()
        {
            // The import is parsed before the importer declares the
            // constant; v1-style parse-order checking let it through, and
            // bump [] changed the constant.
            var (script, output) = Script(
                "import \"lib\"\n" +
                "const MAX = 100\n" +
                "bump()\n" +
                "print MAX\n",
                new Dictionary<string, string>
                {
                    ["lib"] = "fun bump()\n    global MAX\n    MAX = 1\nend\n",
                });

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Cannot reassign constant 'MAX'", result.Diagnostics[0].Message);
            Assert.AreEqual("lib", result.Diagnostics[0].ScriptName);
            Assert.AreEqual(3, result.Diagnostics[0].Line);
            Assert.AreEqual(ScriptErrorKind.CompileError, script.Run().Error.Kind);
            CollectionAssert.IsEmpty(output, "nothing ran");
        }

        [Test]
        public void AnImportsTopLevelWritingTheImportersEnum_IsACompileError()
        {
            var (script, _) = Script(
                "import \"lib\"\n" +
                "enum Team\n" +
                "    RED\n" +
                "end\n",
                new Dictionary<string, string> { ["lib"] = "x = 1\nTeam = 2\n" });

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Cannot reassign constant 'Team'", result.Diagnostics[0].Message);
            Assert.AreEqual("lib", result.Diagnostics[0].ScriptName);
            Assert.AreEqual(2, result.Diagnostics[0].Line);
            Assert.AreEqual(1, result.Diagnostics[0].Column, "at the name");
        }

        [Test]
        public void AnImportReadingTheImportersConstant_Compiles()
        {
            var (script, output) = Script(
                "import \"lib\"\n" +
                "const MAX = 100\n" +
                "print limit()\n",
                new Dictionary<string, string> { ["lib"] = "fun limit()\n    return MAX\nend\n" });

            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "100" }, output);
        }

        [Test]
        public void AFailedCompile_RestoresTheConstants_SoAFixedReloadMayUseTheName()
        {
            var (script, output) = Script("X = 1\nconst X = 2\n");
            Assert.IsFalse(script.Compile().Success);

            script.Reload("X = 5\nprint X\n");

            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "5" }, output);
        }

        [Test]
        public void AHostConstant_IsProtectedToo()
        {
            var (script, _) = Script("fun f()\n    global HOST_MAX\n    HOST_MAX = 1\nend\n");
            script.GlobalMemoryScope.Set("HOST_MAX", WarValue.FromNumeric(FixMath.F64.FromInt(3)));
            script.ConstantNames.Add("HOST_MAX");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Cannot reassign constant 'HOST_MAX'", result.Diagnostics[0].Message);
        }
    }
}
