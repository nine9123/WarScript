// Tests of the migration tool (T3.2, plan 8.2). Compiled into Headless~/Tests
// together with ../Migrator.cs (Unity ignores Tools~); delete with the tool.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using WarScript.Migrate;

namespace Tests
{
    [TestFixture]
    public class MigratorTests
    {
        private static MigrationResult Migrate(params (string Path, string Source)[] files) =>
            MigrateWith(new string[0], files);

        private static MigrationResult MigrateWith(IEnumerable<string> hostGlobals, params (string Path, string Source)[] files) =>
            new Migrator(hostGlobals).Run(files
                .Select(f => new MigrationFile(f.Path, Path.Combine(Path.GetTempPath(), "wsmigrate", f.Path), f.Source))
                .ToList());

        private static string Rewritten(MigrationResult result, string path) =>
            result.Rewritten.TryGetValue(path, out var source) ? source : null;

        private static ReportEntry Single(MigrationResult result, string rule)
        {
            var entries = result.Entries.Where(e => e.Rule == rule).ToList();
            Assert.AreEqual(1, entries.Count, string.Join("\n", result.Entries.Select(e => e.Location + " " + e.Rule + ": " + e.Action)));
            return entries[0];
        }

        // ── 8.4 examples ─────────────────────────────────────────────────

        [Test]
        public void FunctionWritingAGlobal_GetsAGlobalDeclaration()
        {
            var result = Migrate(("a.ws",
                "count = 10\n" +
                "fun helper []\n" +
                "    count = 0\n" +
                "    loop i in 0..3\n" +
                "        count += 1\n" +
                "    end\n" +
                "    return count\n" +
                "end\n"));
            Assert.AreEqual(
                "count = 10\n" +
                "fun helper []\n" +
                "    global count\n" +
                "    count = 0\n" +
                "    loop i in 0..3\n" +
                "        count += 1\n" +
                "    end\n" +
                "    return count\n" +
                "end\n", Rewritten(result, "a.ws"));
            var entry = Single(result, Rules.GlobalWrite);
            Assert.AreEqual(EntryKind.Rewrite, entry.Kind);
            Assert.AreEqual("a.ws:2:1", entry.Location);
            Assert.AreEqual("fun helper []", entry.SourceLine);
        }

        [Test]
        public void GlobalDeclaration_ListsNamesInOrderOfFirstUse_AndSkipsLocalsAndParameters()
        {
            var result = Migrate(("a.ws",
                "a = 1\nb = 2\nc = 3\nx = 4\n" +
                "fun f [x, d = 1]\n" +
                "    t = 1\n" +
                "    print c\n" +
                "    b = c\n" +
                "    x = 2\n" +
                "    loop a in 0..2\n" +
                "    end\n" +
                "    c = 5\n" +
                "end\n"));
            StringAssert.Contains("fun f [x, d = 1]\n    global c, b\n    t = 1\n", Rewritten(result, "a.ws"));
        }

        [Test]
        public void LambdaWritingAGlobal_GetsAGlobalDeclaration_OnItsLine()
        {
            var result = Migrate(("a.ws", "n = 0\nf = fun [x] n = x end\n"));
            Assert.AreEqual("n = 0\nf = fun [x] global n n = x end\n", Rewritten(result, "a.ws"));
            Assert.AreEqual("a.ws:2:5", Single(result, Rules.GlobalWrite).Location);
        }

        [Test]
        public void ANameAssignedOnlyInATopLevelBlock_IsNotAGlobal()
        {
            // v1: the block variable is gone when the function runs, so the
            // function's assignment made a local.
            var result = Migrate(("a.ws", "if true\n    n = 1\nend\nfun f []\n    n = 2\nend\n"));
            Assert.IsNull(Rewritten(result, "a.ws"));
        }

        [Test]
        public void MethodFields_BecomeAtNames()
        {
            var result = Migrate(("a.ws",
                "class Unit [hp]\n" +
                "    armor = 2\n" +
                "    fun hit [d]\n" +
                "        dealt = d - armor\n" +
                "        hp = hp - dealt\n" +
                "        return hp\n" +
                "    end\n" +
                "end\n"));
            Assert.AreEqual(
                "class Unit [hp]\n" +
                "    armor = 2\n" +
                "    fun hit [d]\n" +
                "        dealt = d - @armor\n" +
                "        @hp = @hp - dealt\n" +
                "        return @hp\n" +
                "    end\n" +
                "end\n", Rewritten(result, "a.ws"));
            var entries = result.Entries.Where(e => e.Rule == Rules.FieldAccess).ToList();
            Assert.AreEqual(2, entries.Count);
            StringAssert.Contains("(3 uses)", entries[1].Action);
        }

        [Test]
        public void MethodFields_IncludeBaseClassFields_AndNotParametersOrLoopVariables()
        {
            var result = Migrate(("a.ws",
                "class Base [hp]\nend\n" +
                "class Unit [name] : Base [name]\n" +
                "    fun f [name]\n" +
                "        loop armor in 0..2\n" +
                "            print hp + name + armor\n" +
                "        end\n" +
                "    end\n" +
                "end\n"));
            StringAssert.Contains("print @hp + name + armor", Rewritten(result, "a.ws"));
        }

        [Test]
        public void FieldsInInterpolation_BecomeAtNames()
        {
            var result = Migrate(("a.ws", "class C [x]\n    fun f []\n        return \"x={x}!\"\n    end\nend\n"));
            StringAssert.Contains("return \"x={@x}!\"", Rewritten(result, "a.ws"));
        }

        [Test]
        public void CompoundAssignmentOfAField_GetsOneAt()
        {
            var result = Migrate(("a.ws", "class C [n]\n    fun f []\n        n += 1\n    end\nend\n"));
            StringAssert.Contains("        @n += 1\n", Rewritten(result, "a.ws"));
        }

        [Test]
        public void MethodsAndClassBodies_UsingGlobals_AreReportedNotRewritten()
        {
            var result = Migrate(("a.ws",
                "g = 5\nh = 1\n" +
                "class C\n" +
                "    k = g\n" +
                "    fun m []\n" +
                "        h = 2\n" +
                "        return g\n" +
                "    end\n" +
                "end\n"));
            Assert.IsNull(Rewritten(result, "a.ws"));
            Assert.AreEqual("a.ws:6:9", Single(result, Rules.MethodWritesGlobal).Location);
            var reads = result.Entries.Where(e => e.Rule == Rules.MethodReadsGlobal).Select(e => e.Location).ToList();
            CollectionAssert.AreEqual(new[] { "a.ws:4:9", "a.ws:7:16" }, reads);
            Assert.IsTrue(result.Entries.All(e => e.Kind == EntryKind.Manual));
        }

        [Test]
        public void TextSubtraction_BecomesTextRemove()
        {
            var result = Migrate(("a.ws", "title = \"x\"\nname = \"Sir \" + title - \"Sir \"\n"));
            Assert.AreEqual("title = \"x\"\nname = Text_remove [\"Sir \" + title, \"Sir \"]\n", Rewritten(result, "a.ws"));
            Assert.AreEqual("a.ws:2:23", Single(result, Rules.TextSubtraction).Location);
        }

        [TestCase("y = a - \"x\" - \"y\"", "y = Text_remove [Text_remove [a, \"x\"], \"y\"]")]
        [TestCase("y = (a + b) - \"x\"", "y = Text_remove [(a + b), \"x\"]")]
        [TestCase("y = f[a, \"x\" - b]", "y = f[a, Text_remove [\"x\", b]]")]
        [TestCase("y = \"{a}b\" - \"b\"   # note", "y = Text_remove [\"{a}b\", \"b\"]   # note")]
        [TestCase("y = a{0} - \"b\"", "y = Text_remove [a{0}, \"b\"]")]
        public void TextSubtraction_Shapes(string line, string expected)
        {
            var result = Migrate(("a.ws", "a = \"ab\"\nb = \"b\"\n" + line + "\n"));
            Assert.AreEqual("a = \"ab\"\nb = \"b\"\n" + expected + "\n", Rewritten(result, "a.ws"));
        }

        [TestCase("a -= \"x\"")]
        [TestCase("y = 5 - \"5\"")]
        public void TextSubtraction_ThatCannotBeRewritten_IsManual(string line)
        {
            var result = Migrate(("a.ws", "a = \"ab\"\n" + line + "\n"));
            Assert.IsNull(Rewritten(result, "a.ws"));
            Assert.AreEqual(EntryKind.Manual, Single(result, Rules.TextSubtraction).Kind);
        }

        [Test]
        public void NumericSubtraction_IsNotTouched()
        {
            var result = Migrate(("a.ws", "a = 3\ny = a - 1\n"));
            Assert.IsNull(Rewritten(result, "a.ws"));
            Assert.AreEqual(0, result.Entries.Count);
        }

        [Test]
        public void TopLevelBlockVariable_ReadAfterTheBlock_IsReported()
        {
            var result = Migrate(("a.ws",
                "if true\n    made = 1\nend\nprint made\n" +
                "loop i in 0..2\n    row = i\nend\nloop row in {1}\n    print row\nend\n"));
            var entry = Single(result, Rules.BlockEscape);
            Assert.AreEqual("a.ws:4:7", entry.Location);
            StringAssert.Contains("line 1", entry.Action);
        }

        [Test]
        public void DynamicPropertyAndTextIndex_AreReported()
        {
            var result = Migrate(("a.ws",
                "class C [a]\n    b = 1\nend\nc = new C [1]\nc :: a = 2\nc :: b = 3\nc :: z = 4\n" +
                "s = \"abc\"\ns{0} = \"z\"\narr = {1}\narr{0} = 2\n"));
            Assert.AreEqual("a.ws:7:3", Single(result, Rules.DynamicProperty).Location);
            Assert.AreEqual("a.ws:9:2", Single(result, Rules.TextIndex).Location);
        }

        [Test]
        public void AVariableNamedGlobal_IsRenamed()
        {
            var result = Migrate(("a.ws", "global = 1\nprint \"{global}\"\n"));
            Assert.AreEqual("global_ = 1\nprint \"{global_}\"\n", Rewritten(result, "a.ws"));
            Assert.AreEqual(2, result.Entries.Count(e => e.Rule == Rules.GlobalName));
        }

        [Test]
        public void AFunctionWritingAGlobalNamedGlobal_DeclaresTheNewName()
        {
            var result = Migrate(("a.ws", "global = 1\nfun f [x]\n    global = x\nend\n"));
            Assert.AreEqual("global_ = 1\nfun f [x]\n    global global_\n    global_ = x\nend\n", Rewritten(result, "a.ws"));
            StringAssert.Contains("`global global_`", Single(result, Rules.GlobalWrite).Action);
        }

        // ── Imports ──────────────────────────────────────────────────────

        [Test]
        public void ImportAfterDefinitions_MovesToTheTop()
        {
            var result = Migrate(
                ("a.ws", "# header\nimport \"b\"\nfun f []\nend\nimport \"c\"  # c\nprint 1\n"),
                ("b.ws", "x = 1\n"),
                ("c.ws", "y = 1\n"));
            Assert.AreEqual("# header\nimport \"b\"\nimport \"c\"  # c\nfun f []\nend\nprint 1\n", Rewritten(result, "a.ws"));
            Assert.AreEqual(EntryKind.Rewrite, Single(result, Rules.ImportOrder).Kind);
        }

        [Test]
        public void ImportAfterAStatement_IsManual()
        {
            var result = Migrate(("a.ws", "x = 1\nimport \"b\"\n"), ("b.ws", "y = 1\n"));
            Assert.IsNull(Rewritten(result, "a.ws"));
            Assert.AreEqual(EntryKind.Manual, Single(result, Rules.ImportOrder).Kind);
        }

        [Test]
        public void UnresolvedImport_IsReported()
        {
            var result = Migrate(("a.ws", "import \"missing\"\n"));
            Assert.AreEqual("a.ws:1:1", Single(result, Rules.UnresolvedImport).Location);
        }

        [Test]
        public void GlobalsOfImportingFiles_CountForTheImportedOne()
        {
            var result = Migrate(
                ("main.ws", "import \"lib\"\nscore = 0\n"),
                ("lib.ws", "fun add []\n    score = score + 1\nend\n"),
                ("other.ws", "fun add []\n    score = 1\nend\n"));
            Assert.AreEqual("fun add []\n    global score\n    score = score + 1\nend\n", Rewritten(result, "lib.ws"));
            Assert.IsNull(Rewritten(result, "other.ws"), "not in main's module set");
        }

        [Test]
        public void HostGlobals_CountAsGlobals()
        {
            var result = MigrateWith(new[] { "SPEED" }, ("a.ws", "fun f []\n    SPEED = 2\nend\n"));
            StringAssert.Contains("    global SPEED\n", Rewritten(result, "a.ws"));
        }

        // ── Files ────────────────────────────────────────────────────────

        [Test]
        public void ParseError_IsReported_AndTheFileLeftAlone()
        {
            var result = Migrate(("a.ws", "x = (1 + 2\n"));
            var entry = Single(result, Rules.ParseError);
            Assert.AreEqual("a.ws:1:5", entry.Location);
            Assert.AreEqual(0, result.Rewritten.Count);
        }

        [Test]
        public void CrLfFiles_KeepCrLf()
        {
            var result = Migrate(("a.ws", "n = 0\r\nfun f []\r\n    n = 1\r\nend\r\n"));
            Assert.AreEqual("n = 0\r\nfun f []\r\n    global n\r\n    n = 1\r\nend\r\n", Rewritten(result, "a.ws"));
        }

        [Test]
        public void Report_ListsEveryEntry_WithLocationRuleAndSourceLine()
        {
            var result = Migrate(("a.ws", "n = 0\nfun f []\n    n = 1\nend\n"));
            var report = Report.Markdown(result, new[] { "a.ws" }, rewritten: false);
            StringAssert.Contains("| `global-write` | A function or lambda writes a global | 1 | 0 |", report);
            StringAssert.Contains("- `a.ws:2:1` **global-write** (rewrite): Inserted `global n`", report);
            StringAssert.Contains("  fun f []\n", report);
        }

        [Test]
        public void GoldenCorpus_MigratesWithoutParseErrorsBeyondTheExpectedOnes()
        {
            var root = CorpusRoot();
            var files = Directory.EnumerateFiles(root, "*.ws", SearchOption.AllDirectories)
                .OrderBy(f => f, System.StringComparer.Ordinal)
                .Select(f => new MigrationFile(f.Substring(root.Length).Replace('\\', '/'), f, File.ReadAllText(f)))
                .ToList();
            var result = new Migrator(new string[0]).Run(files);
            // The only files the v1 frontend rejects are the ones that test
            // exactly that ('# expect-compile-error:').
            foreach (var e in result.Entries.Where(e => e.Rule == Rules.ParseError))
                StringAssert.Contains("expect-compile-error", File.ReadAllText(Path.Combine(root, e.File.TrimStart('/'))), e.Location);
            Assert.Greater(result.Rewritten.Count, 0);
        }

        private static string CorpusRoot([CallerFilePath] string sourceFilePath = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath), "..", "..", "..", "Tests", "Golden~"));
    }
}
