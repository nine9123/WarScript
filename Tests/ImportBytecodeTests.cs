using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarScript;

namespace Tests
{
    /// <summary>
    /// The import side of the bytecode pipeline: a host that precompiled its
    /// scripts answers <c>bytecodeResolver</c> for an imported path and the VM
    /// loads that instead of lexing, parsing and compiling the source — which
    /// is where most of a project's script code usually is.
    ///
    /// These tests deliberately withhold the source (no fileResolver) wherever
    /// the point is that the front end never ran: a script that imports a path
    /// only the bytecode resolver answers can only have come from bytecode.
    /// The fallbacks — no bytecode, unusable bytecode, a throwing resolver —
    /// are covered against a host that supplies both.
    /// </summary>
    [TestFixture]
    public class ImportBytecodeTests
    {
        /// <summary>
        /// Compile a script the way a build step would, and hand back the
        /// bytes a host would ship. The libraries are registered first for the
        /// same reason a mod build registers its natives: a named-argument
        /// call is resolved against the callee's declared parameters while
        /// parsing, so the signatures have to be in scope at compile time.
        /// </summary>
        private static byte[] Compile(string source)
        {
            var script = new WarScriptLanguage("precompiled", source, null, null);
            WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);

            var stream = new MemoryStream();
            script.SaveBytecode(stream);
            return stream.ToArray();
        }

        private static (WarScriptLanguage script, List<string> output) Run(
            string source,
            Dictionary<string, byte[]> bytecode,
            Dictionary<string, string> files = null)
        {
            return TestHelper.Run("import_bytecode_test", source,
                setupScope: (s, scope) => WarScriptLibraryRegistry.RegisterAll(s, scope),
                fileResolver: files == null
                    ? (System.Func<string, string>)null
                    : path => files.TryGetValue(path, out var src) ? src : null,
                bytecodeResolver: path => bytecode.TryGetValue(path, out var bytes) ? bytes : null);
        }

        // ── The bytecode is what runs ──

        [Test]
        public void ImportedBytecode_MakesFunctionsAvailable()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["lib"] = Compile("fun helper [x] return x * 10 end")
            };
            var (_, output) = Run("import \"lib\"\nprint helper [2]", bytecode);
            Assert.AreEqual("20", output[0]);
        }

        [Test]
        public void ImportedBytecode_MakesClassesAvailable()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["shapes"] = Compile(
                    "class Rect [w, h]\n" +
                    "fun area []\n" +
                    "return this :: w * this :: h\n" +
                    "end\n" +
                    "end")
            };
            var (_, output) = Run(
                "import \"shapes\"\nr = new Rect [3, 4]\nprint r :: area []", bytecode);
            Assert.AreEqual("12", output[0]);
        }

        [Test]
        public void ImportedBytecode_ExecutesTopLevelCode()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["noisy"] = Compile("print \"loaded\"\nfun f [] return 1 end")
            };
            var (_, output) = Run("import \"noisy\"\nprint f []", bytecode);
            Assert.AreEqual(new[] { "loaded", "1" }, output);
        }

        [Test]
        public void ImportedBytecode_CanCallNativeFunctions()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["mathy"] = Compile("fun hypot [a, b] return sqrt [a * a + b * b] end")
            };
            // sqrt is a fixed-point approximation, so the comparison is made
            // in-script rather than by parsing the printed text
            var (_, output) = Run(
                "import \"mathy\"\nprint abs [hypot [3, 4] - 5] < 0.001", bytecode);
            Assert.AreEqual("True", output[0]);
        }

        [Test]
        public void ImportedBytecode_NestedImportAlsoLoadsBytecode()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["outer"] = Compile("import \"inner\"\nfun outer_fn [] return inner_fn [] + 1 end"),
                ["inner"] = Compile("fun inner_fn [] return 41 end")
            };
            var (_, output) = Run("import \"outer\"\nprint outer_fn []", bytecode);
            Assert.AreEqual("42", output[0]);
        }

        [Test]
        public void ImportedBytecode_NestedImportFallsBackToSource()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["outer"] = Compile("import \"inner\"\nfun outer_fn [] return inner_fn [] + 1 end")
            };
            var files = new Dictionary<string, string>
            {
                ["inner"] = "fun inner_fn [] return 41 end"
            };
            var (_, output) = Run("import \"outer\"\nprint outer_fn []", bytecode, files);
            Assert.AreEqual("42", output[0]);
        }

        [Test]
        public void ImportedBytecode_IsPreferredOverSource()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["lib"] = Compile("fun which [] return \"bytecode\" end")
            };
            var files = new Dictionary<string, string>
            {
                ["lib"] = "fun which [] return \"source\" end"
            };
            var (_, output) = Run("import \"lib\"\nprint which []", bytecode, files);
            Assert.AreEqual("bytecode", output[0]);
        }

        [Test]
        public void ImportedBytecode_SamePathTwice_OnlyLoadedOnce()
        {
            var bytes = Compile("print \"loaded\"\nfun f [] return 1 end");
            var resolverCalls = 0;

            var (_, output) = TestHelper.Run("import_bytecode_test",
                "import \"once\"\nimport \"once\"\nprint f []",
                bytecodeResolver: _ => { resolverCalls++; return bytes; });

            Assert.AreEqual(new[] { "loaded", "1" }, output);
            Assert.AreEqual(1, resolverCalls);
        }

        [Test]
        public void ImportedBytecode_Circular_IsScriptError()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["a"] = Compile("import \"b\""),
                ["b"] = Compile("import \"a\"")
            };
            var (_, output) = Run("import \"a\"\nprint \"after\"", bytecode);
            StringAssert.Contains("Circular import", output[0]);
            Assert.IsFalse(output.Contains("after"));
        }

        // ── Falling back to the source ──

        [Test]
        public void NoBytecodeForPath_CompilesSource()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = "fun helper [x] return x * 10 end"
            };
            var (_, output) = Run("import \"lib\"\nprint helper [2]",
                new Dictionary<string, byte[]>(), files);
            Assert.AreEqual("20", output[0]);
        }

        [Test]
        public void UnusableBytecode_WarnsAndCompilesSource()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["lib"] = new byte[] { 1, 2, 3, 4, 5 }
            };
            var files = new Dictionary<string, string>
            {
                ["lib"] = "fun helper [x] return x * 10 end"
            };
            var (_, output) = Run("import \"lib\"\nprint helper [2]", bytecode, files);

            StringAssert.Contains("could not be loaded", output[0]);
            StringAssert.Contains("lib", output[0]);
            Assert.AreEqual("20", output[1]);
        }

        [Test]
        public void UnusableBytecode_WithoutSource_IsScriptError()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["lib"] = new byte[] { 1, 2, 3, 4, 5 }
            };
            var (_, output) = Run("import \"lib\"\nprint \"after\"", bytecode);

            StringAssert.Contains("could not be loaded", output[0]);
            StringAssert.Contains("no file resolver", output[1]);
            Assert.IsFalse(output.Contains("after"));
        }

        [Test]
        public void ResolverThrows_WarnsAndCompilesSource()
        {
            var (_, output) = TestHelper.Run("import_bytecode_test",
                "import \"lib\"\nprint helper [2]",
                fileResolver: _ => "fun helper [x] return x * 10 end",
                bytecodeResolver: _ => throw new IOException("disk error"));

            StringAssert.Contains("disk error", output[0]);
            Assert.AreEqual("20", output[1]);
        }

        [Test]
        public void EmptyBytecode_IsTreatedAsNone()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["lib"] = new byte[0]
            };
            var files = new Dictionary<string, string>
            {
                ["lib"] = "fun helper [x] return x * 10 end"
            };
            var (_, output) = Run("import \"lib\"\nprint helper [2]", bytecode, files);
            Assert.AreEqual("20", output[0]);
        }

        // ── The tree-walk statement, which predates the VM ──

        /// <summary>
        /// <see cref="WarScript.Statement.ImportStatement"/> is what the AST
        /// interpreter runs; the compiler turns an import into an opcode, so
        /// nothing in a normal Run() reaches it. It resolves bytecode the same
        /// way, and runs it on a VM because only a VM can.
        /// </summary>
        private static List<string> RunTreeWalkImport(
            string path,
            Dictionary<string, byte[]> bytecode,
            Dictionary<string, string> files = null)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("tree_walk", "",
                files == null ? (System.Func<string, string>)null
                    : p => files.TryGetValue(p, out var src) ? src : null,
                (s, msg) => output.Add(msg),
                p => bytecode.TryGetValue(p, out var bytes) ? bytes : null);
            WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);

            script.DefinitionContext.PushScope(script.GlobalDefinitionScope);
            script.MemoryContext.PushScope(script.GlobalMemoryScope);
            try
            {
                new WarScript.Statement.ImportStatement(script, 1, "tree_walk", path).Execute();
            }
            finally
            {
                script.DefinitionContext.EndScope();
                script.MemoryContext.EndScope();
            }

            return output;
        }

        [Test]
        public void TreeWalkImport_LoadsBytecode()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["lib"] = Compile("print \"loaded from bytecode\"")
            };
            Assert.AreEqual(new[] { "loaded from bytecode" }, RunTreeWalkImport("lib", bytecode));
        }

        [Test]
        public void TreeWalkImport_UnusableBytecode_WarnsAndCompilesSource()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["lib"] = new byte[] { 1, 2, 3, 4, 5 }
            };
            var files = new Dictionary<string, string>
            {
                ["lib"] = "print \"loaded from source\""
            };
            var output = RunTreeWalkImport("lib", bytecode, files);

            StringAssert.Contains("could not be loaded", output[0]);
            Assert.AreEqual("loaded from source", output[1]);
        }

        [Test]
        public void MissingFromBothResolvers_IsScriptError()
        {
            var (_, output) = Run("import \"nope\"\nprint \"after\"",
                new Dictionary<string, byte[]>(), new Dictionary<string, string>());
            StringAssert.Contains("Import 'nope' not found", output[0]);
        }
    }
}
