using System.Collections.Generic;
using NUnit.Framework;
using WarScript;

namespace Tests
{
    /// <summary>
    /// <c>new C [...]</c> at compile time (v2 plan T3.8, PROP-29): the class
    /// must be a class of the module set or the host, taking that many
    /// arguments; checked once every file of the module set is compiled,
    /// so an import may instantiate a class of its importer. At runtime a
    /// wrong number of arguments is an ArgumentError. The golden cases are
    /// <c>language/new_*.ws</c> and <c>errors/constructor_arity.ws</c>.
    /// </summary>
    [TestFixture]
    public class ConstructorCheckTests
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
        public void WrongArity_IsACompileError_AtTheClassName_AndInstallsNothing()
        {
            var (script, _) = Script(
                "class Unit(team, hp)\n" +
                "end\n" +
                "fun spawn()\n" +
                "    return 1 + (new Unit(\"red\"))\n" +
                "end\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            var error = result.Diagnostics[0];
            Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
            Assert.AreEqual("Class 'Unit' takes 2 arguments, not 1", error.Message);
            Assert.AreEqual("main", error.ScriptName);
            Assert.AreEqual(4, error.Line);
            Assert.AreEqual(21, error.Column, "at the class name");
            Assert.IsNull(script.GetFunction("spawn", 0));
            Assert.AreEqual(ScriptErrorKind.CompileError, script.Run().Error.Kind);
        }

        [Test]
        public void SeveralClassesOfOneName_TakeAnyOfTheirArities()
        {
            // Nested classes of different classes may share a name; 'new'
            // compiles when one of them takes that many arguments.
            var (script, output) = Script(
                "class Car\n" +
                "    class Part(name)\n" +
                "    end\n" +
                "    fun part()\n" +
                "        return new Part(\"wheel\")\n" +
                "    end\n" +
                "end\n" +
                "class Boat\n" +
                "    class Part\n" +
                "    end\n" +
                "end\n" +
                "print (new Car()).part().name\n" +
                "x = new Part(1, 2)\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Class 'Part' takes 0 or 1 arguments, not 2", result.Diagnostics[0].Message);
            Assert.AreEqual(13, result.Diagnostics[0].Line);
        }

        [Test]
        public void AnImport_MayInstantiateAClassOfItsImporter()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = "fun make()\n    return new Unit(\"red\")\nend\n",
            };
            var (script, output) = Script(
                "import \"lib\"\n" +
                "class Unit(team)\n" +
                "end\n" +
                "print make().team\n", files);

            Assert.IsTrue(script.Compile().Success);
            Assert.IsTrue(script.Run().IsOk);
            CollectionAssert.AreEqual(new[] { "red" }, output);
        }

        [Test]
        public void AWrongArityInAnImport_NamesTheImport()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = "x = 1\nfun make()\n    return new Unit()\nend\n",
            };
            var (script, _) = Script(
                "import \"lib\"\n" +
                "class Unit(team)\n" +
                "end\n", files);

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            var error = result.Diagnostics[0];
            Assert.AreEqual("Class 'Unit' takes 1 argument, not 0", error.Message);
            Assert.AreEqual("lib", error.ScriptName);
            Assert.AreEqual(3, error.Line);
            var run = script.Run();
            Assert.AreEqual(ScriptErrorKind.CompileError, run.Error.Kind);
            Assert.AreEqual("    return new Unit()", run.Error.SourceLine, "the import's line, not the importer's");
        }

        [Test]
        public void AnUndefinedClass_IsACompileError()
        {
            var (script, _) = Script("class Unit\nend\nu = new Unti()\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Class 'Unti' is not defined", result.Diagnostics[0].Message);
            Assert.AreEqual(3, result.Diagnostics[0].Line);
        }

        [Test]
        public void ANestedNewOfAnUnknownClass_IsLeftToTheVM()
        {
            // The instance's class decides what 'obj :: new C' finds; an
            // instance of another script is a TypeError there (6.7).
            var (script, _) = Script("fun nested(u)\n    return u.new Part(1)\nend\n");

            Assert.IsTrue(script.Compile().Success);
        }

        [Test]
        public void WrongArityAtRuntime_IsARescuableArgumentError()
        {
            var (script, output) = Script(
                "class Car\n" +
                "    class Part(name)\n" +
                "    end\n" +
                "end\n" +
                "class Boat\n" +
                "    class Part\n" +
                "    end\n" +
                "end\n" +
                "begin\n" +
                "    p = (new Boat()).new Part(\"sail\")\n" +
                "rescue e\n" +
                "    print e\n" +
                "end\n" +
                "p = (new Boat()).new Part(\"sail\")\n");

            var result = script.Run();

            CollectionAssert.AreEqual(new[] { "Class 'Part' takes 0 arguments, not 1" }, output);
            Assert.AreEqual(ScriptErrorKind.ArgumentError, result.Error.Kind);
            Assert.AreEqual(14, result.Error.Line);
        }
    }
}
