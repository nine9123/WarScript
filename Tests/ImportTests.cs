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

        /// <summary>
        /// Compiles <paramref name="source"/> without running it (T3.7:
        /// imports are resolved by Compile()).
        /// </summary>
        private static (WarScript.WarScriptLanguage script, WarScript.CompileResult result, List<string> output) Compile(
            string source, System.Func<string, string> fileResolver)
        {
            var output = new List<string>();
            var script = new WarScript.WarScriptLanguage("import_test", source, fileResolver, (_, msg) => output.Add(msg));
            return (script, script.Compile(), output);
        }

        private static System.Func<string, string> Files(Dictionary<string, string> files) =>
            path => files.TryGetValue(path, out var src) ? src : null;

        private static void AssertCompileError(WarScript.CompileResult result, string message, string scriptName, int line)
        {
            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.Diagnostics.Count);
            StringAssert.Contains(message, result.Diagnostics[0].Message);
            Assert.AreEqual(scriptName, result.Diagnostics[0].ScriptName);
            Assert.AreEqual(line, result.Diagnostics[0].Line);
        }

        [Test]
        public void Import_Circular_IsACompileErrorNamingTheCycle()
        {
            var files = new Dictionary<string, string>
            {
                ["a"] = "import \"b\"\nprint \"a\"",
                ["b"] = "# b\nimport \"c\"\nimport \"a\"",
                ["c"] = "print \"c\""
            };
            var (script, result, output) = Compile("import \"a\"\nprint \"after\"", Files(files));

            AssertCompileError(result, "Circular import: a -> b -> a", "b", 3);
            Assert.AreEqual(WarScript.ScriptErrorKind.CompileError, script.Run().Error.Kind);
            CollectionAssert.DoesNotContain(output, "c");
            CollectionAssert.DoesNotContain(output, "after");
        }

        [Test]
        public void Import_FunctionDefinedAgainInTheImporter_IsACompileErrorNamingTheImport()
        {
            var files = new Dictionary<string, string> { ["lib"] = "\nfun helper [x] return x end" };
            var (_, result, _) = Compile("import \"lib\"\nfun helper [y] return y end", Files(files));
            AssertCompileError(result, "Function 'helper' with 1 argument is already defined in 'lib' at line 2", "import_test", 2);
        }

        [Test]
        public void Import_ClassDefinedInTwoImports_IsACompileErrorInTheSecond()
        {
            var files = new Dictionary<string, string>
            {
                ["a"] = "class Unit end",
                ["b"] = "fun other [] return 1 end\nclass Unit [hp] end"
            };
            var (_, result, _) = Compile("import \"a\"\nimport \"b\"", Files(files));
            AssertCompileError(result, "Class 'Unit' is already defined in 'a' at line 1", "b", 2);
        }

        [Test]
        public void Import_SameFileThroughTwoImports_IsNotADuplicate()
        {
            var files = new Dictionary<string, string>
            {
                ["a"] = "import \"common\"",
                ["b"] = "import \"common\"",
                ["common"] = "class Unit end\nfun shared [] return 7 end"
            };
            var (_, output) = Run("import \"a\"\nimport \"b\"\nu = new Unit\nprint shared []", files);
            Assert.AreEqual(new[] { "7" }, output);
        }

        [Test]
        public void Import_FunctionsOfTheSameNameWithOtherArities_AreNotDuplicates()
        {
            var files = new Dictionary<string, string> { ["lib"] = "fun f [a] return a end" };
            var (_, output) = Run("import \"lib\"\nfun f [] return 0 end\nprint f [] + f [4]", files);
            Assert.AreEqual(new[] { "4" }, output);
        }

        [Test]
        public void Import_RecompileAfterReload_DoesNotSeeTheOldDefinitions()
        {
            var files = new Dictionary<string, string> { ["lib"] = "fun f [] return 1 end" };
            var (script, result, _) = Compile("import \"lib\"\nfun g [] return f [] end", Files(files));
            Assert.IsTrue(result.Success);
            script.Reload("import \"lib\"\nfun g [] return f [] + 1 end");
            Assert.IsTrue(script.Compile().Success);
        }

        [Test]
        public void Import_Definitions_AreTheScriptsOnceItCompiles()
        {
            // 5.8: one namespace for the module set, before any top-level
            // code runs; the import's top level still runs at Run.
            var files = new Dictionary<string, string>
            {
                ["lib"] = "import \"inner\"\nprint \"lib loaded\"\nfun helper [x] return inner [x] * 10 end",
                ["inner"] = "class Box [v] end\nfun inner [x] return x + 1 end"
            };
            var (script, result, output) = Compile("import \"lib\"\nprint \"main\"", Files(files));
            Assert.IsTrue(result.Success);

            var helper = script.GetFunction("helper", 1);
            Assert.IsNotNull(helper);
            Assert.IsNotNull(script.GetFunction("inner", 1), "an import's import is in the namespace too");
            var called = script.Call(helper, WarScript.Expression.Value.WarValue.FromNumeric(FixMath.F64.FromInt(2)));
            Assert.IsTrue(called.IsOk, called.Error?.ToString());
            Assert.IsTrue(called.Value.Numeric == 30);
            CollectionAssert.IsEmpty(output, "no top-level code ran");

            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "lib loaded", "main" }, output);
        }

        [Test]
        public void Import_SaveBytecode_HoldsOnlyTheScriptsOwnDefinitions()
        {
            var files = new Dictionary<string, string> { ["lib"] = "class Unit end\nfun helper [] return 7 end" };
            const string source = "import \"lib\"\nfun own [] return helper [] end\nprint own []";
            var (script, result, _) = Compile(source, Files(files));
            Assert.IsTrue(result.Success);

            var beforeRun = new System.IO.MemoryStream();
            script.SaveBytecode(beforeRun);
            Assert.IsTrue(script.Run().IsOk);
            var afterRun = new System.IO.MemoryStream();
            script.SaveBytecode(afterRun);
            CollectionAssert.AreEqual(beforeRun.ToArray(), afterRun.ToArray(), "running does not add the import's definitions to the file");

            // The import's definitions come from the import when it runs.
            var output = new List<string>();
            var loaded = new WarScript.WarScriptLanguage("import_test", "", Files(files), (_, msg) => output.Add(msg));
            loaded.LoadBytecode(new System.IO.MemoryStream(afterRun.ToArray()));
            Assert.IsNull(loaded.GetFunction("helper", 0));
            Assert.IsTrue(loaded.Run().IsOk);
            Assert.AreEqual(new[] { "7" }, output);
            Assert.IsNotNull(loaded.GetFunction("helper", 0));
        }

        [Test]
        public void Import_ShadowingANative_LogsOnce()
        {
            // Joining the namespace (5.8) copies the import's definitions into
            // the script's scope; that copy is not a second shadowing.
            var files = new Dictionary<string, string> { ["lib"] = "fun twice [a] return a end" };
            var (_, output) = TestHelper.Run("import_test", "import \"lib\"\nprint twice [3]",
                setupScope: (s, scope) => scope.AddFunction(new WarScript.Context.Definition.NativeFunctionDefinition(
                    new WarScript.Context.Definition.FunctionDetails("twice", new List<string> { "a" }), args => args[0], "", "")),
                fileResolver: path => files.TryGetValue(path, out var src) ? src : null);
            Assert.AreEqual(new[] { "Shadowing native function 'twice'", "3" }, output);
        }

        [Test]
        public void Import_OfItself_IsACompileError()
        {
            var files = new Dictionary<string, string> { ["a"] = "import \"a\"" };
            AssertCompileError(Compile("import \"a\"", Files(files)).result, "Circular import: a -> a", "a", 1);
        }

        [Test]
        public void Import_MissingFile_IsACompileError()
        {
            var (_, result, _) = Compile("import \"nope\"\nprint \"after\"", Files(new Dictionary<string, string>()));
            AssertCompileError(result, "Import 'nope' not found", "import_test", 1);
        }

        [Test]
        public void Import_MissingFileInAnImport_IsACompileErrorInThatFile()
        {
            var files = new Dictionary<string, string> { ["lib"] = "\n\nimport \"nope\"" };
            AssertCompileError(Compile("import \"lib\"", Files(files)).result, "Import 'nope' not found", "lib", 3);
        }

        [Test]
        public void Import_WithoutResolver_IsACompileError()
        {
            AssertCompileError(Compile("import \"lib\"\nprint \"after\"", null).result,
                "Cannot import 'lib': no file resolver", "import_test", 1);
        }

        [Test]
        public void Import_ResolverThrows_IsACompileError()
        {
            var (_, result, _) = Compile("import \"boom\"\nprint \"after\"",
                _ => throw new System.IO.IOException("disk error"));
            AssertCompileError(result, "Failed to read import 'boom': disk error", "import_test", 1);
        }

        [Test]
        public void Import_SyntaxErrorInImportedFile_IsFoundByCompile()
        {
            var files = new Dictionary<string, string> { ["broken"] = "x = 1\nfun f [\nreturn 1\nend" };
            var (_, result, output) = Compile("print \"main\"\n", Files(files));
            Assert.IsTrue(result.Success, "no import, nothing resolved");

            (_, result, output) = Compile("import \"broken\"\nprint \"main\"", Files(files));
            Assert.IsFalse(result.Success);
            Assert.AreEqual("broken", result.Diagnostics[0].ScriptName);
            CollectionAssert.IsEmpty(output);
        }

        [Test]
        public void Import_ResolverIsAskedOncePerPath_AtCompileTime()
        {
            var asked = new List<string>();
            var files = new Dictionary<string, string>
            {
                ["a"] = "import \"c\"\nfun fa [] return 1 end",
                ["b"] = "import \"c\"\nfun fb [] return 2 end",
                ["c"] = "fun fc [] return 3 end"
            };
            var (script, result, _) = Compile("import \"a\"\nimport \"b\"\nprint fa [] + fb [] + fc []",
                path => { asked.Add(path); return Files(files)(path); });

            Assert.IsTrue(result.Success);
            Assert.AreEqual(new[] { "a", "c", "b" }, asked);
            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "a", "c", "b" }, asked, "running resolves nothing again");
        }

        [Test]
        public void Import_TopLevelCode_RunsDependenciesFirst_OncePerInstance()
        {
            // 5.8: depth-first post-order over the imports in textual order.
            var files = new Dictionary<string, string>
            {
                ["a"] = "import \"c\"\nprint \"a\"",
                ["b"] = "import \"c\"\nimport \"d\"\nprint \"b\"",
                ["c"] = "print \"c\"",
                ["d"] = "print \"d\""
            };
            var (_, output) = Run("import \"a\"\nimport \"b\"\nprint \"main\"", files);
            Assert.AreEqual(new[] { "c", "a", "d", "b", "main" }, output);
        }

        [Test]
        public void Import_NamedArguments_ResolveAgainstTheImportedFunction()
        {
            // The importer is parsed after its imports (DEC-7), so a named
            // argument call is put in the imported function's parameter
            // order, also between two imports of the same file.
            var files = new Dictionary<string, string>
            {
                ["lib"] = "fun span [from, to] return to - from end",
                ["user"] = "fun use [] return span [to: 10, from: 4] end"
            };
            var (_, output) = Run("import \"lib\"\nimport \"user\"\nprint span [to: 5, from: 2]\nprint use []", files);
            Assert.AreEqual(new[] { "3", "6" }, output);
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

        [Test]
        public void Import_BaseParameterBoundTwice_IsACompileError()
        {
            // Q-16: the bases come from an import, which Compile() has
            // compiled before the importer (T3.7), so the bindings are checked
            // at compile time like those of the file's own classes.
            var files = new Dictionary<string, string>
            {
                ["ops"] = "class Sub [a, b]\nend\nclass Mul [a, b]\nend"
            };
            var (_, result, _) = Compile(
                "import \"ops\"\n" +
                "class Calculator [p, q] : Sub [q, p], Mul [p, q]\nend\n" +
                "c = new Calculator [2, 10]",
                Files(files));
            AssertCompileError(result, "'a' is bound twice in class 'Calculator': base class 'Mul' binds its parameter 'a' to a different argument", "import_test", 2);
        }

        [Test]
        public void Import_FieldOfAnImportedBase_IsCheckedAtCompileTime()
        {
            var files = new Dictionary<string, string> { ["animal"] = "class Animal [name]\n    legs = 4\nend" };
            var (_, result, _) = Compile(
                "import \"animal\"\n" +
                "class Bird [name] : Animal [name]\n    fun wings []\n        return @wingz\n    end\nend",
                Files(files));
            AssertCompileError(result, "'wingz' is not a field of class 'Bird'", "import_test", 4);
        }

        [Test]
        public void Import_BaseFromAnImportsImport_IsCheckedInTheImport()
        {
            // 'shapes' derives from a class of its own import 'base'.
            var files = new Dictionary<string, string>
            {
                ["base"] = "class Shape [id]\nend",
                ["shapes"] = "import \"base\"\nclass Square [id, side] : Shape [id]\n    fun area []\n        return @sid * @sid\n    end\nend"
            };
            var (_, result, _) = Compile("import \"shapes\"\nprint 1", Files(files));
            AssertCompileError(result, "'sid' is not a field of class 'Square'", "shapes", 4);
        }

        [Test]
        public void Import_BaseParameterBoundOnce_IsOneField()
        {
            var files = new Dictionary<string, string>
            {
                ["animal"] = "class Animal [name]\n    legs = 4\n    fun describe []\n        return \"{@name} has {@legs} legs\"\n    end\nend"
            };
            var (_, output) = Run(
                "import \"animal\"\n" +
                "class Bird [name] : Animal [name]\n    @legs = 2\nend\n" +
                "print (new Bird [\"Tweety\"]) :: describe []",
                files);
            Assert.AreEqual(new[] { "Tweety has 2 legs" }, output);
        }
    }
}
