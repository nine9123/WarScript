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
        /// Imports are resolved when compiling (T3.7), so the build has to
        /// find them: here every imported path is an empty file. The saved
        /// bytecode holds only this file's definitions.
        /// </summary>
        private static byte[] Compile(string source)
        {
            var script = new WarScriptLanguage("precompiled", source, _ => "", null);
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
            Assert.AreEqual("true", output[0]);
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
        public void ImportedBytecode_NestedImport_IsInTheScriptsNamespace()
        {
            // 'inner' is listed in 'outer''s module header (format 10), so
            // Compile resolves it; its definitions reach the script, not only
            // 'outer'.
            var bytecode = new Dictionary<string, byte[]>
            {
                ["outer"] = Compile("import \"inner\"\nfun outer_fn [] return 1 end"),
                ["inner"] = Compile("fun inner_fn [] return 41 end")
            };
            var (script, output) = Run("import \"outer\"\nprint inner_fn [] + outer_fn []", bytecode);
            Assert.AreEqual("42", output[0]);
            Assert.IsNotNull(script.GetFunction("inner_fn", 0));
        }

        [Test]
        public void ImportedBytecode_TopLevelCode_RunsDependenciesFirst_OncePerInstance()
        {
            // No Import instruction (format 11): the module headers give the
            // order, a depth-first post-order in textual order (5.8), and Run
            // runs each import's top level before the script's, once.
            var bytecode = new Dictionary<string, byte[]>
            {
                ["outer"] = Compile("import \"inner\"\nimport \"shared\"\nprint \"outer\""),
                ["inner"] = Compile("import \"shared\"\nprint \"inner\""),
                ["shared"] = Compile("print \"shared\"")
            };
            var (script, output) = Run("import \"outer\"\nimport \"shared\"\nprint \"main\"", bytecode);
            Assert.AreEqual(new[] { "shared", "inner", "outer", "main" }, output);

            output.Clear();
            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "main" }, output);
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
        public void ImportedBytecode_Circular_IsACompileError()
        {
            // The files list their imports (format 10, T3.7), so the cycle is
            // found before anything runs, in the file that closes it.
            var bytecode = new Dictionary<string, byte[]>
            {
                ["a"] = Compile("import \"b\""),
                ["b"] = Compile("import \"a\"")
            };
            var e = Assert.Throws<WarScript.Exception.SyntaxException>(() => Run("import \"a\"\nprint \"after\"", bytecode));
            StringAssert.Contains("Circular import: a -> b -> a", e.Message);
            Assert.AreEqual("b", e.ScriptName);
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
        public void UnusableBytecode_WithoutSource_IsACompileError()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["lib"] = new byte[] { 1, 2, 3, 4, 5 }
            };
            var output = new List<string>();
            var script = new WarScriptLanguage("import_bytecode_test", "import \"lib\"\nprint \"after\"", null,
                (_, msg) => output.Add(msg), path => bytecode.TryGetValue(path, out var bytes) ? bytes : null);
            var result = script.Compile();

            StringAssert.Contains("could not be loaded", output[0]);
            Assert.IsFalse(result.Success);
            StringAssert.Contains("Cannot import 'lib': no file resolver", result.Diagnostics[0].Message);
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

        [Test]
        public void MissingFromBothResolvers_IsACompileError()
        {
            var e = Assert.Throws<WarScript.Exception.SyntaxException>(() => Run("import \"nope\"\nprint \"after\"",
                new Dictionary<string, byte[]>(), new Dictionary<string, string>()));
            StringAssert.Contains("Import 'nope' not found", e.Message);
        }

        [Test]
        public void ImportOfLoadedBytecode_IsResolvedAtCompileTime()
        {
            // A loaded file lists its imports (format 10, T3.7): its missing
            // import is a compile error of the importing script, in that file.
            var bytecode = new Dictionary<string, byte[]>
            {
                ["outer"] = Compile("import \"inner\"\nfun outer_fn [] return 1 end")
            };
            var e = Assert.Throws<WarScript.Exception.SyntaxException>(() =>
                Run("import \"outer\"\nprint \"after\"", bytecode, new Dictionary<string, string>()));
            StringAssert.Contains("Import 'inner' not found", e.Message);
            Assert.AreEqual("outer", e.ScriptName);
        }

        [Test]
        public void ImportOfLoadedBytecode_IsInTheNamespaceBeforeRun()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["outer"] = Compile("import \"inner\"\nprint \"outer\"\nfun outer_fn [] return inner_fn [] + 1 end")
            };
            var files = new Dictionary<string, string> { ["inner"] = "print \"inner\"\nfun inner_fn [] return 1 end" };
            var output = new List<string>();
            var script = new WarScriptLanguage("main", "import \"outer\"\nprint outer_fn []",
                path => files.TryGetValue(path, out var src) ? src : null, (_, m) => output.Add(m),
                path => bytecode.TryGetValue(path, out var bytes) ? bytes : null);

            Assert.IsTrue(script.Compile().Success);
            Assert.IsNotNull(script.GetFunction("inner_fn", 0), "the bytecode import's import is resolved by Compile");
            var result = script.Run();
            Assert.IsTrue(result.IsOk, result.Error?.ToString());
            Assert.AreEqual(new[] { "inner", "outer", "2" }, output);
        }

        [Test]
        public void ImportOfLoadedBytecode_DefiningAFunctionTheScriptDefines_IsACompileError()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["outer"] = Compile("import \"inner\"")
            };
            var files = new Dictionary<string, string> { ["inner"] = "fun f [] return 1 end" };
            var e = Assert.Throws<WarScript.Exception.SyntaxException>(() =>
                Run("import \"outer\"\nfun f [] return 2 end", bytecode, files));
            StringAssert.Contains("Function 'f' with 0 arguments is already defined in 'inner'", e.Message);
        }

        [Test]
        public void TwoBytecodeImports_DefiningTheSameFunction_IsACompileErrorAtTheSecondImport()
        {
            var bytecode = new Dictionary<string, byte[]>
            {
                ["a"] = Compile("fun helper [x] return 1 end"),
                ["b"] = Compile("fun helper [y] return 2 end")
            };
            var e = Assert.Throws<WarScript.Exception.SyntaxException>(
                () => Run("import \"a\"\nimport \"b\"", bytecode));
            StringAssert.Contains("Cannot import 'b': Function 'helper' with 1 argument is already defined in 'a'", e.Message);
            Assert.AreEqual(2, e.Line);
        }

        [Test]
        public void FunctionOfABytecodeImport_DefinedAgain_IsACompileError()
        {
            var bytecode = new Dictionary<string, byte[]> { ["lib"] = Compile("class Unit end") };
            var e = Assert.Throws<WarScript.Exception.SyntaxException>(
                () => Run("import \"lib\"\nclass Unit [hp] end", bytecode));
            StringAssert.Contains("Class 'Unit' is already defined in 'lib'", e.Message);
            Assert.AreEqual(2, e.Line);
        }

        [Test]
        public void BaseParameterBoundTwice_ThroughABytecodeImport_IsARescuableTypeErrorAtNew()
        {
            // Q-16: a bytecode import's classes have no source, so the
            // compiler cannot check the bindings; 'new' raises the error.
            var bytecode = new Dictionary<string, byte[]> { ["ops"] = Compile("class Sub [a, b]\nend\nclass Mul [a, b]\nend") };
            var (_, output) = Run(
                "import \"ops\"\n" +
                "class Calculator [p, q] : Sub [q, p], Mul [p, q]\nend\n" +
                "begin\n    c = new Calculator [2, 10]\nrescue e\n    print e\nend",
                bytecode);
            Assert.AreEqual(new[] { "'a' is bound twice in class 'Calculator': base class 'Mul' binds its parameter 'a' to a different argument" }, output);
        }
    }
}
