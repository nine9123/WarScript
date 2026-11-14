using System.Collections.Generic;
using NUnit.Framework;

namespace Tests
{
    /// <summary>
    /// The import system had zero test coverage. These tests exercise the
    /// bytecode VM's import path end-to-end through the host-provided
    /// fileResolver: definition copying, top-level execution, caching,
    /// nesting, and every failure mode (no resolver, missing file, resolver
    /// exception, circular imports).
    /// </summary>
    [TestFixture]
    public class ImportTests
    {
        private static (WarScript.WarScriptLanguage script, List<string> output) Run(
            string source, Dictionary<string, string> files)
        {
            return TestHelper.Run("import_test", source,
                fileResolver: path => files.TryGetValue(path, out var src) ? src : null);
        }

        [Test]
        public void Import_MakesFunctionsAvailable()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = "fun helper [x] return x * 10 end"
            };
            var (_, output) = Run("import \"lib\"\nprint helper [2]", files);
            Assert.AreEqual("20", output[0]);
        }

        [Test]
        public void Import_MakesClassesAvailable()
        {
            var files = new Dictionary<string, string>
            {
                ["shapes"] =
                    "class Rect [w, h]\n" +
                    "fun area []\n" +
                    "return this :: w * this :: h\n" +
                    "end\n" +
                    "end"
            };
            var (_, output) = Run(
                "import \"shapes\"\nr = new Rect [3, 4]\nprint r :: area []",
                files);
            Assert.AreEqual("12", output[0]);
        }

        [Test]
        public void Import_ExecutesTopLevelCodeOfImportedFile()
        {
            var files = new Dictionary<string, string>
            {
                ["noisy"] = "print \"loaded\"\nfun f [] return 1 end"
            };
            var (_, output) = Run("import \"noisy\"\nprint f []", files);
            Assert.AreEqual(new[] { "loaded", "1" }, output);
        }

        [Test]
        public void Import_SamePathTwice_OnlyExecutesOnce()
        {
            var files = new Dictionary<string, string>
            {
                ["once"] = "print \"loaded\"\nfun f [] return 1 end"
            };
            var (_, output) = Run(
                "import \"once\"\nimport \"once\"\nprint f []",
                files);
            Assert.AreEqual(new[] { "loaded", "1" }, output);
        }

        [Test]
        public void Import_Nested_TransitiveDefinitionsAvailable()
        {
            var files = new Dictionary<string, string>
            {
                ["outer"] = "import \"inner\"\nfun outer_fn [] return inner_fn [] + 1 end",
                ["inner"] = "fun inner_fn [] return 41 end"
            };
            var (_, output) = Run("import \"outer\"\nprint outer_fn []", files);
            Assert.AreEqual("42", output[0]);
        }

        [Test]
        public void Import_Circular_IsScriptError()
        {
            var files = new Dictionary<string, string>
            {
                ["a"] = "import \"b\"",
                ["b"] = "import \"a\""
            };
            var (script, output) = Run("import \"a\"\nprint \"after\"", files);
            StringAssert.Contains("Circular import", output[0]);
            Assert.IsFalse(output.Contains("after"));
        }

        [Test]
        public void Import_MissingFile_IsScriptError()
        {
            var (_, output) = Run("import \"nope\"\nprint \"after\"",
                new Dictionary<string, string>());
            StringAssert.Contains("Import 'nope' not found", output[0]);
        }

        [Test]
        public void Import_WithoutResolver_IsScriptError()
        {
            var (_, output) = TestHelper.Run("no_resolver", "import \"lib\"\nprint \"after\"");
            StringAssert.Contains("no file resolver", output[0]);
        }

        [Test]
        public void Import_ResolverThrows_IsScriptError()
        {
            var (_, output) = TestHelper.Run("resolver_throws",
                "import \"boom\"\nprint \"after\"",
                fileResolver: _ => throw new System.IO.IOException("disk error"));
            StringAssert.Contains("Failed to read import 'boom'", output[0]);
            StringAssert.Contains("disk error", output[0]);
        }

        [Test]
        public void Import_SyntaxErrorInImportedFile_Throws()
        {
            var files = new Dictionary<string, string>
            {
                ["broken"] = "fun f [\nreturn 1\nend"
            };
            Assert.Throws<WarScript.Exception.SyntaxException>(
                () => Run("import \"broken\"", files));
        }

        // v1 allowed an import anywhere, so a missing file could be rescued;
        // v2 allows imports only at the top of a file (T3.3, 5.8).
        [TestCase("begin\nimport \"lib\"\nrescue e\nend", "'import' is allowed only at the top of a file")]
        [TestCase("fun f []\n    import \"lib\"\nend", "'import' is allowed only at the top of a file")]
        [TestCase("print 1\nimport \"lib\"", "'import' must come before every other statement")]
        [TestCase("fun f [] return 1 end\nimport \"lib\"", "'import' must come before every other statement")]
        public void Import_AnywhereButTheTopOfTheFile_IsASyntaxError(string source, string message)
        {
            var files = new Dictionary<string, string> { ["lib"] = "x = 1" };
            var e = Assert.Throws<WarScript.Exception.SyntaxException>(() => Run(source, files));
            StringAssert.Contains(message, e.Message);
        }

        [Test]
        public void Import_AfterComments_IsAccepted()
        {
            var files = new Dictionary<string, string> { ["lib"] = "fun helper [x] return x * 10 end" };
            var (_, output) = Run("# header\n\n# more\nimport \"lib\"\nimport \"lib\"\nprint helper [3]", files);
            Assert.AreEqual("30", output[0]);
        }
    }
}
