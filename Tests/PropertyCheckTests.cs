using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Statement;

namespace Tests
{
    /// <summary>
    /// <c>obj :: x</c> and <c>E :: X</c> at compile time (v2 plan T3.8,
    /// PROP-29): <c>x</c> must be a field or a method of some class of the
    /// module set or the host, and <c>X</c> a member of the enum <c>E</c>
    /// (or <c>values</c>, <c>names</c>, <c>count</c>, <c>name</c>); checked
    /// once every file of the module set is compiled. The golden cases are
    /// <c>language/property_*.ws</c>, <c>method_unknown.ws</c> and
    /// <c>enum_member_*.ws</c>.
    /// </summary>
    [TestFixture]
    public class PropertyCheckTests
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
        public void AnUnknownProperty_IsACompileError_AtItsName_AndInstallsNothing()
        {
            var (script, _) = Script(
                "class Unit [hp]\n" +
                "end\n" +
                "fun probe [u]\n" +
                "    return 1 + u :: hpp\n" +
                "end\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            var error = result.Diagnostics[0];
            Assert.AreEqual("'hpp' is not a field or method of any class", error.Message);
            Assert.AreEqual("main", error.ScriptName);
            Assert.AreEqual(4, error.Line);
            Assert.AreEqual(21, error.Column, "at the property name");
            Assert.IsNull(script.GetFunction("probe", 1));
            Assert.AreEqual(ScriptErrorKind.CompileError, script.Run().Error.Kind);
        }

        [Test]
        public void AnImport_MayReadAFieldOfAClassOfItsImporter()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = "fun team_of [u]\n    return u :: team\nend\n",
            };
            var (script, output) = Script(
                "import \"lib\"\n" +
                "class Unit [team]\n" +
                "end\n" +
                "print team_of [new Unit [\"red\"]]\n", files);

            Assert.IsTrue(script.Compile().Success);
            Assert.IsTrue(script.Run().IsOk);
            CollectionAssert.AreEqual(new[] { "red" }, output);
        }

        [Test]
        public void AnUnknownMemberOfAnImportedEnum_NamesTheImporter()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = "enum Team\n    RED\n    BLUE\nend\n",
            };
            var (script, _) = Script(
                "import \"lib\"\n" +
                "print Team :: BLUE\n" +
                "print Team :: GREEN\n", files);

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("'GREEN' is not a member of enum 'Team'", result.Diagnostics[0].Message);
            Assert.AreEqual("main", result.Diagnostics[0].ScriptName);
            Assert.AreEqual(3, result.Diagnostics[0].Line);
        }

        [Test]
        public void AnUnknownPropertyInAnImport_NamesTheImport()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = "x = 1\nfun team_of [u]\n    return u :: taem\nend\n",
            };
            var (script, _) = Script(
                "import \"lib\"\n" +
                "class Unit [team]\n" +
                "end\n", files);

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("'taem' is not a field or method of any class", result.Diagnostics[0].Message);
            Assert.AreEqual("lib", result.Diagnostics[0].ScriptName);
            Assert.AreEqual(3, result.Diagnostics[0].Line);
            Assert.AreEqual("    return u :: taem", script.Run().Error.SourceLine, "the import's line");
        }

        [Test]
        public void AnEnumInsideAClass_IsNotCheckedAsAnEnum()
        {
            // Only an enum at the top level of a file is a global the
            // compiler can name; 'Kind' here is a field of the instance.
            var (script, _) = Script(
                "class Unit\n" +
                "    enum Kind\n" +
                "        A\n" +
                "    end\n" +
                "    fun probe []\n" +
                "        return @Kind :: A\n" +
                "    end\n" +
                "end\n" +
                "x = (new Unit []) :: Kind :: A\n");

            Assert.IsTrue(script.Compile().Success, string.Join("; ", script.Compile().Diagnostics));
        }

        [Test]
        public void AHostEnum_IsKnownByTheFieldsOfItsInstance()
        {
            // What the binding generator emits for a [WsEnum]: a class
            // registered without fields, its instance a host constant.
            var source = "print Team :: RED\nprint Team :: name [0]\nx = Team :: GREEN\n";
            var (script, _) = Script(source);
            RegisterHostEnum(script, "Team", "RED");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("'GREEN' is not a field or method of any class", result.Diagnostics[0].Message);
            Assert.AreEqual(3, result.Diagnostics[0].Line);
        }

        [Test]
        public void AHostClassWithoutFields_TurnsTheFieldCheckOff()
        {
            // The VM accepts any field of such a class, so the compiler
            // cannot prove a name unknown.
            var (script, _) = Script("fun probe [u]\n    return u :: anything\nend\n");
            var details = new ClassDetails("Gadget", new List<string>());
            script.GlobalDefinitionScope.AddClass(new ClassDefinition(details, new List<ClassDetails>(),
                new ClassStatement(script, 0, "Gadget"), new DefinitionScope(script, null)));

            Assert.IsTrue(script.Compile().Success);
        }

        private static void RegisterHostEnum(WarScriptLanguage script, string name, string member)
        {
            var details = new ClassDetails(name, new List<string>());
            var scope = new DefinitionScope(script, null);
            var definition = new ClassDefinition(details, new List<ClassDetails>(), new ClassStatement(script, 0, name), scope);
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("name", new List<string> { "value" }), _ => WarValue.FromText(member), "", ""));
            script.GlobalDefinitionScope.AddClass(definition);
            var data = new ClassData(definition, new MemoryScope(script, null), new Dictionary<string, ClassData>());
            data.Relations[name] = data;
            data.SetProperty(member, WarValue.FromNumeric(0));
            script.GlobalMemoryScope.Set(name, WarValue.FromClass(data));
            script.ConstantNames.Add(name);
        }
    }
}
