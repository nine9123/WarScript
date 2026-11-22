using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarScript;
using WarScript.Bytecode;
using WarScript.Context.Definition;
using WarScript.Native;

namespace Tests
{
    /// <summary>
    /// The indexed field list (v2 plan 5.7, T3.4): every class's own fields
    /// get a fixed index in its <see cref="ClassDetails"/>, the constructor
    /// parameters first (at their <c>PropertyIndex</c>), then the other
    /// names its body assigns in ordinal order, the same from source and
    /// from bytecode.
    /// </summary>
    [TestFixture]
    public class FieldIndexTests
    {
        private const string Source =
            "class Unit [hp, name]\n" +
            "    zeta = 0\n" +
            "    @armor = 1\n" +
            "    alpha = hp\n" +
            "    fun heal [n]\n" +
            "        local = n\n" +
            "        @hp = @hp + local\n" +
            "    end\n" +
            "end\n" +
            "class Hero [hp, name, title] : Unit [hp, name]\n" +
            "    rank = 1\n" +
            "end\n";

        private static readonly string[] UnitFields = { "hp", "name", "alpha", "armor", "zeta" };

        private static void AssertIndexed(ClassDetails details, params string[] expected)
        {
            CollectionAssert.AreEqual(expected, details.Fields, details.Name);
            Assert.AreEqual(expected.Length, details.FieldIndex.Count, details.Name);
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(i, details.FieldIndex[expected[i]], details.Name + " " + expected[i]);
            foreach (var parameter in details.PropertyIndex)
                Assert.AreEqual(parameter.Value, details.FieldIndex[parameter.Key], "a parameter keeps its index");
        }

        [Test]
        public void SourceClass_IndexesItsParametersFirst_ThenItsBodyFieldsInOrdinalOrder()
        {
            var (_, definitions) = new WarScriptLanguage("t", Source, null, null).CompileWithoutRunning();

            AssertIndexed(definitions.GetClass("Unit").ClassDetails, UnitFields);
        }

        [Test]
        public void DerivedClass_IndexesOnlyItsOwnFields()
        {
            // The base's fields are held by the base's class part, which its
            // own details index.
            var (_, definitions) = new WarScriptLanguage("t", Source, null, null).CompileWithoutRunning();

            AssertIndexed(definitions.GetClass("Hero").ClassDetails, "hp", "name", "title", "rank");
        }

        [Test]
        public void LoadedClass_HasTheSameIndicesAsTheSource()
        {
            var (body, definitions) = new WarScriptLanguage("t", Source, null, null).CompileWithoutRunning();
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                BytecodeSerializer.Save(w, body, definitions);
            ms.Position = 0;
            DefinitionScope loaded;
            using (var r = new BinaryReader(ms))
                (_, loaded) = BytecodeSerializer.Load(r, new WarScriptLanguage("u", "", null, null), null);

            AssertIndexed(loaded.GetClass("Unit").ClassDetails, UnitFields);
            AssertIndexed(loaded.GetClass("Hero").ClassDetails, "hp", "name", "title", "rank");
        }

        [Test]
        public void HostClass_WithoutKnownFields_IndexesItsParameters()
        {
            var details = new ClassDetails("Host", new List<string> { "b", "a" });

            AssertIndexed(details, "b", "a");
        }

        [Test]
        public void ManifestClass_IndexesItsFieldsLine()
        {
            var script = new WarScriptLanguage("main", "", null, null);
            NativeManifest.Parse("warscript-manifest 2\nclass Unit kind\nfields Unit hp armor\n").Register(script);

            AssertIndexed(script.GlobalDefinitionScope.GetClass("Unit").ClassDetails, "kind", "armor", "hp");
        }

        [Test]
        public void SettingTheFieldsAgain_ReplacesTheIndex()
        {
            var details = new ClassDetails("C", new List<string> { "p" });
            var definition = new ClassDefinition(details, new List<ClassDetails>(), null, null);

            definition.Fields = new HashSet<string> { "p", "y", "x" };
            AssertIndexed(details, "p", "x", "y");
            definition.Fields = null;
            AssertIndexed(details, "p");
        }
    }
}
