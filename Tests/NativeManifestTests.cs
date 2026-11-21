using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Tests.Golden;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// The native manifest (v2 plan T3.8, PROP-30): what a host provides,
    /// without the implementations, so that a script compiles against the
    /// game's natives where the game is not running. The generator's side is
    /// checked by <c>.claude-task/tools/check-binding-generator.sh</c>.
    /// </summary>
    [TestFixture]
    public class NativeManifestTests
    {
        private const string Sample =
            "# the game\n" +
            "warscript-manifest 1\n" +
            "function spawn_unit 1 kind team\n" +
            "global player\n" +
            "const MAX_UNITS\n" +
            "enum Team Red=0 Blue=1 Neutral=-7\n" +
            "class Unit kind\n" +
            "fields Unit hp\n" +
            "method Unit move 2 x y\n";

        private static (WarScriptLanguage script, List<string> output) Script(string source, NativeManifest manifest)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("main", source, null, (s, msg) => output.Add(msg));
            script.LogUnhandledErrors = false;
            manifest.Register(script);
            return (script, output);
        }

        private static string FirstDiagnostic(CompileResult result) =>
            result.Diagnostics.Count == 0 ? null : result.Diagnostics[0].Message;

        [Test]
        public void Parse_ThenToString_WritesTheSameEntriesSorted()
        {
            var manifest = NativeManifest.Parse(Sample);

            Assert.AreEqual(1, manifest.Functions.Count);
            Assert.AreEqual(1, manifest.Functions[0].MinArity);
            CollectionAssert.AreEqual(new[] { "kind", "team" }, manifest.Functions[0].Parameters);
            CollectionAssert.AreEqual(new[] { "player" }, manifest.Globals);
            CollectionAssert.AreEqual(new[] { "MAX_UNITS" }, manifest.Constants);
            Assert.AreEqual(-7, manifest.Enums[0].Members[2].Value);
            CollectionAssert.AreEqual(new[] { "hp" }, manifest.Classes[0].Fields);
            Assert.AreEqual("move", manifest.Classes[0].Methods[0].Name);

            var text = manifest.ToString();
            Assert.AreEqual(Sample.Substring("# the game\n".Length), text);
            Assert.AreEqual(text, NativeManifest.Parse(text).ToString());
        }

        [TestCase("function f 0\n", "line 1: expected the header")]
        [TestCase("warscript-manifest 1\nfunction f x a\n", "line 2: min arity 'x' is not a number")]
        [TestCase("warscript-manifest 1\nfunction f 2 a\n", "line 2:")]
        [TestCase("warscript-manifest 1\nmethod C m 0\n", "line 2: 'method' names class 'C'")]
        [TestCase("warscript-manifest 1\nenum E A=x\n", "line 2: enum member 'A=x'")]
        [TestCase("warscript-manifest 1\nglobal a  b\n", "line 2: names are separated by single spaces")]
        [TestCase("warscript-manifest 1\nnative f\n", "line 2: unknown entry 'native'")]
        [TestCase("# nothing\n", "no header")]
        public void Parse_RejectsMalformedText_NamingTheLine(string text, string message)
        {
            var e = Assert.Throws<FormatException>(() => NativeManifest.Parse(text));
            StringAssert.Contains(message, e.Message);
        }

        [Test]
        public void AScriptUsingTheManifest_Compiles_AndItsMistakesAreCompileErrors()
        {
            var manifest = NativeManifest.Parse(Sample);
            var good = Script(
                "u = spawn_unit [\"orc\"]\n" +
                "v = new Unit [\"x\"]\n" +
                "v :: move [1, 2]\n" +
                "print v :: hp\n" +
                "print player\n" +
                "print MAX_UNITS\n" +
                "print Team :: Neutral\n", manifest).script.Compile();
            Assert.IsTrue(good.Success, FirstDiagnostic(good));

            Assert.AreEqual("Function 'spawn_unit' takes 1 or 2 arguments, not 3",
                FirstDiagnostic(Script("spawn_unit [1, 2, 3]\n", manifest).script.Compile()));
            Assert.AreEqual("Class 'Unit' takes 1 argument, not 0",
                FirstDiagnostic(Script("u = new Unit []\n", manifest).script.Compile()));
            Assert.AreEqual("'speed' is not a field or method of any class",
                FirstDiagnostic(Script("fun f [u]\n    return u :: speed\nend\n", manifest).script.Compile()));
            Assert.AreEqual("Cannot reassign constant 'MAX_UNITS'",
                FirstDiagnostic(Script("MAX_UNITS = 3\n", manifest).script.Compile()));
            Assert.AreEqual("undefined variable 'nobody'",
                FirstDiagnostic(Script("print nobody\n", manifest).script.Compile()));
            Assert.AreEqual("Function 'spawn_unit' with 1 argument shadows a native function",
                FirstDiagnostic(Script("fun spawn_unit [k]\n    return k\nend\n", manifest).script.Compile()));
        }

        [Test]
        public void AClassWithoutAFieldsLine_AcceptsAnyProperty()
        {
            var manifest = NativeManifest.Parse("warscript-manifest 1\nclass Unit kind\n");
            var result = Script("fun f [u]\n    return u :: anything\nend\n", manifest).script.Compile();
            Assert.IsTrue(result.Success, FirstDiagnostic(result));
        }

        [Test]
        public void AStubThatRuns_RaisesARescuableError_AndAnEnumWorks()
        {
            var (script, output) = Script(
                "print Team :: name [1]\n" +
                "print Team :: values\n" +
                "print player\n" +
                "begin\n" +
                "    spawn_unit [\"orc\"]\n" +
                "rescue e\n" +
                "    print e\n" +
                "end\n", NativeManifest.Parse(Sample));

            var result = script.Run();

            Assert.IsTrue(result.IsOk, result.Error?.ToString());
            Assert.AreEqual("Blue", output[0]);
            Assert.AreEqual("[0, 1, -7]", output[1]);
            Assert.AreEqual("null", output[2]);
            StringAssert.Contains("Native function 'spawn_unit' failed", output[3]);
            StringAssert.Contains("only declared by a native manifest", output[3]);
        }

        [Test]
        public void ARegisteredNative_WinsOverTheManifest()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("main", "print abs [-2]\nprint MAX\n", null, (s, msg) => output.Add(msg));
            WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);
            script.GlobalMemoryScope.Set("MAX", WarValue.FromNumeric(5));
            NativeManifest.Parse("warscript-manifest 1\nfunction abs 1 x\nconst MAX\n").Register(script);

            Assert.IsTrue(script.Run().IsOk);
            CollectionAssert.AreEqual(new[] { "2", "5" }, output);
        }

        [Test]
        public void LoadBytecodeAndReload_KeepTheManifestsConstantsAndEnums()
        {
            var (script, _) = Script("x = 1\n", NativeManifest.Parse(Sample));
            var bytecode = new MemoryStream();
            script.SaveBytecode(bytecode);
            bytecode.Position = 0;
            script.LoadBytecode(bytecode);
            Assert.IsTrue(script.ConstantNames.Contains("MAX_UNITS"));
            Assert.IsTrue(script.ConstantNames.Contains("Team"));

            Assert.AreEqual("Cannot reassign constant 'MAX_UNITS'",
                FirstDiagnostic(ReloadAndCompile(script, "MAX_UNITS = 3\n")));
            Assert.AreEqual("Cannot reassign constant 'Team'",
                FirstDiagnostic(ReloadAndCompile(script, "Team = 3\n")));
        }

        private static CompileResult ReloadAndCompile(WarScriptLanguage script, string source)
        {
            try { script.Reload(source); }
            catch (WarScript.Exception.SyntaxException) { }
            return script.Compile();
        }

        [Test]
        public void FromScript_CapturesNativesGlobalsConstantsEnumsAndClasses()
        {
            var host = new WarScriptLanguage("host", "", null, (s, m) => { });
            NativeManifest.Parse(Sample).Register(host);
            host.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("heal", new List<string> { "unit", "amount" }, 1),
                args => WarValue.Null, "", ""));

            var captured = NativeManifest.FromScript(host).ToString();

            Assert.AreEqual(
                "warscript-manifest 1\n" +
                "function heal 1 unit amount\n" +
                "function spawn_unit 1 kind team\n" +
                "global player\n" +
                "const MAX_UNITS\n" +
                "enum Team Red=0 Blue=1 Neutral=-7\n" +
                "class Unit kind\n" +
                "fields Unit hp kind\n" +
                "method Unit move 2 x y\n", captured);
        }

        /// <summary>
        /// The stdlib's manifest stands in for the stdlib: every golden that
        /// uses the stdlib compiles against it exactly as against the real
        /// natives (the same error or warnings, or none).
        /// </summary>
        [Test]
        public void TheStdlibsManifest_CompilesEveryGoldenLikeTheStdlib()
        {
            var host = new WarScriptLanguage("host", "", null, (s, m) => { });
            WarScriptLibraryRegistry.RegisterAll(host, host.GlobalDefinitionScope);
            var manifest = NativeManifest.Parse(NativeManifest.FromScript(host).ToString());
            Assert.Greater(manifest.Functions.Count, 20);

            var root = GoldenRunner.GoldenRoot();
            var compared = 0;
            foreach (var path in Directory.GetFiles(root, "*.ws", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                var relative = path.Substring(root.Length + 1).Replace('\\', '/');
                var real = GoldenFile.Load(root, relative);
                if (real.NoStdlib) continue;
                real.CompileOnly = true;
                var stub = GoldenFile.Load(root, relative);
                stub.CompileOnly = true;
                stub.NoStdlib = true;
                stub.Manifest = manifest;

                var expected = GoldenRun.Execute(real, GoldenMode.Source);
                var actual = GoldenRun.Execute(stub, GoldenMode.Source);
                Assert.AreEqual(expected.CompileError, actual.CompileError, relative);
                Assert.AreEqual(expected.EscapedException, actual.EscapedException, relative);
                CollectionAssert.AreEqual(expected.Warnings.Select(w => w.Script + ":" + w.Line + ": " + w.Message),
                    actual.Warnings.Select(w => w.Script + ":" + w.Line + ": " + w.Message), relative);
                compared++;
            }
            Assert.Greater(compared, 100);
        }

        [Test]
        [Category("HeadlessOnly")]
        public void TheRunner_ChecksAScriptAgainstAManifest()
        {
            var runner = IsolatedRun.RunnerPath();
            if (runner == null) Assert.Ignore("The runner is built by the headless test project only.");
            var dir = Path.Combine(Path.GetTempPath(), "ws-manifest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "game.wsmanifest"), Sample);
                File.WriteAllText(Path.Combine(dir, "good.ws"), "u = spawn_unit [\"orc\"]\n");
                File.WriteAllText(Path.Combine(dir, "bad.ws"), "x = 1\nu = spawn_unit []\n");

                var (goodExit, good) = RunRunner(runner, dir, "good.ws");
                Assert.AreEqual(0, goodExit, good);
                StringAssert.Contains("\"status\": \"ok\"", good);
                StringAssert.Contains("\"hash\": null", good);

                var (badExit, bad) = RunRunner(runner, dir, "bad.ws");
                Assert.AreEqual(0, badExit, bad);
                StringAssert.Contains("\"status\": \"compile-error\"", bad);
                StringAssert.Contains("bad.ws:2:5: error: Function 'spawn_unit' takes 1 or 2 arguments, not 0", bad);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static (int exit, string output) RunRunner(string runner, string dir, string script)
        {
            var info = new ProcessStartInfo
            {
                FileName = IsolatedRun.DotnetHost(),
                Arguments = "\"" + runner + "\" \"" + Path.Combine(dir, script) + "\" --manifest \""
                            + Path.Combine(dir, "game.wsmanifest") + "\" --compile-only",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            using (var process = Process.Start(info))
            {
                var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                Assert.IsTrue(process.WaitForExit(IsolatedRun.TimeoutMilliseconds), "the runner timed out");
                return (process.ExitCode, output);
            }
        }
    }
}
