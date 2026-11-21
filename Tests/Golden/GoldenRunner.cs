using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Tests.Golden
{
    /// <summary>
    /// Runs every <c>Tests/Golden~/**/*.ws</c> file and checks the expectations
    /// written in its comments (v2 plan, section 9.2). Each file runs from
    /// source and from bytecode (compile, SaveBytecode, LoadBytecode into a new
    /// instance) under every culture in <see cref="Cultures"/>; every run must
    /// meet the expectations, pass the invariant checks, and print exactly what
    /// the first run printed.
    ///
    /// Files marked <c># isolate</c> run the same matrix, but each run happens in
    /// a child process through <c>WarScript.Runner</c> (plan 9.5), so a script
    /// that kills the process (a .NET stack overflow) fails only its own test.
    ///
    /// Adding a test means adding one .ws file. <c>Golden~</c> ends in <c>~</c>
    /// so Unity does not import it, and the .ws files need no .meta files.
    /// </summary>
    [TestFixture]
    public class GoldenRunner
    {
        /// <summary>Plan section 9.4. Results must be identical in all of them.</summary>
        internal static readonly string[] Cultures = { "", "en-US", "de-DE", "sv-SE", "fi-FI", "tr-TR" };

        public static IEnumerable<TestCaseData> GoldenFiles() => Files(isolated: false);

        public static IEnumerable<TestCaseData> IsolatedGoldenFiles() => Files(isolated: true);

        private static IEnumerable<TestCaseData> Files(bool isolated)
        {
            var root = GoldenRoot();
            if (!Directory.Exists(root))
                yield break;

            foreach (var path in Directory.GetFiles(root, "*.ws", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                var relative = path.Substring(root.Length + 1).Replace('\\', '/');
                if (GoldenFile.Load(root, relative).Isolate != isolated)
                    continue;
                yield return new TestCaseData(relative).SetName("Golden(" + relative + ")");
            }
        }

        [TestCaseSource(nameof(GoldenFiles))]
        public void Golden(string relativePath)
        {
            var file = GoldenFile.Load(GoldenRoot(), relativePath);
            Report(file, Check(file, GoldenRun.Execute, (mode, hash) => RecordHash(relativePath, mode, hash)));
        }

        [TestCaseSource(nameof(IsolatedGoldenFiles))]
        [Category("HeadlessOnly")]
        public void GoldenIsolated(string relativePath)
        {
            var runner = IsolatedRun.RunnerPath();
            if (runner == null)
                Assert.Ignore("'# isolate' files need the child-process runner, which only the headless build provides " +
                              "(Headless~/Runner, plan 9.5).");

            var file = GoldenFile.Load(GoldenRoot(), relativePath);
            Report(file, Check(file, (f, mode, culture) => IsolatedRun.Execute(runner, f, mode, culture),
                               (mode, hash) => RecordHash(relativePath, mode, hash)));
        }

        private static void Report(GoldenFile file, string failure)
        {
            if (file.KnownFailing != null)
            {
                if (failure != null)
                    Assert.Ignore($"Known failing ({file.KnownFailing}): {failure}");
                Assert.Fail($"Marked '# known-failing: {file.KnownFailing}' but passes: remove the marker.");
            }

            if (failure != null)
                Assert.Fail(failure);
        }

        /// <summary>
        /// Runs every mode and culture; returns the first failure, or null.
        /// Every run must also end with the same state hash (plan 9.3);
        /// <paramref name="recordHash"/> receives each mode's invariant-culture hash.
        /// </summary>
        internal static string Check(GoldenFile file, Func<GoldenFile, GoldenMode, string, GoldenRun> execute,
                                     Action<GoldenMode, string> recordHash = null)
        {
            List<string> reference = null;
            string referenceHash = null;
            string referenceRun = null;

            foreach (var mode in file.Modes)
            {
                foreach (var culture in Cultures)
                {
                    var runName = $"[{mode}, {(culture.Length == 0 ? "invariant" : culture)}]";
                    var run = execute(file, mode, culture);
                    if (culture.Length == 0 && run.Hash != null)
                        recordHash?.Invoke(mode, run.Hash);

                    var failure = run.CheckExpectations(file);
                    if (failure != null)
                        return runName + " " + failure;

                    if (reference == null)
                    {
                        reference = run.Output;
                        referenceHash = run.Hash;
                        referenceRun = runName;
                    }
                    else if (!reference.SequenceEqual(run.Output))
                    {
                        return $"{runName} printed something different from {referenceRun}:\n" +
                               GoldenRun.Diff(reference, run.Output);
                    }
                    else if (run.Hash != referenceHash)
                    {
                        return $"{runName} ended with state hash {run.Hash ?? "<none>"}, " +
                               $"{referenceRun} with {referenceHash ?? "<none>"}";
                    }
                }
            }
            return null;
        }

        // ── State hashes (T0.6) ──────────────────────────────────────────

        private static readonly SortedDictionary<string, string> Hashes =
            new SortedDictionary<string, string>(StringComparer.Ordinal);

        private static void RecordHash(string relativePath, GoldenMode mode, string hash)
        {
            lock (Hashes)
                Hashes[$"{relativePath} [{mode.ToString().ToLowerInvariant()}]"] = hash;
        }

        /// <summary>
        /// Writes one <c>&lt;file&gt; [&lt;mode&gt;]: &lt;hash&gt;</c> line per golden run
        /// to <c>Headless~/out/golden-hashes.&lt;os&gt;-&lt;arch&gt;.txt</c>, for
        /// <c>Headless~/compare-hashes.sh</c> to diff against other machines
        /// (plan 9.7, 9.8). Headless only: inside Unity the package folder is read-only.
        /// </summary>
        [OneTimeTearDown]
        public void WriteHashes()
        {
#if WARSCRIPT_HEADLESS
            List<string> lines;
            lock (Hashes)
            {
                if (Hashes.Count == 0) return;
                lines = Hashes.Select(h => h.Key + ": " + h.Value).ToList();
            }

            var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
                   : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
                   : "linux";
            var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            var outDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(GoldenRoot())), "Headless~", "out");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, $"golden-hashes.{os}-{arch}.txt"), string.Join("\n", lines) + "\n");
#endif
        }

        /// <summary>Absolute path of <c>Tests/Golden~/</c>.</summary>
        internal static string GoldenRoot() => GoldenRootFrom();

        private static string GoldenRootFrom([CallerFilePath] string sourceFilePath = "")
        {
            // This file is Tests/Golden/GoldenRunner.cs; the corpus is Tests/Golden~/.
            var testsDir = Path.GetDirectoryName(Path.GetDirectoryName(sourceFilePath));
            return Path.Combine(testsDir, "Golden~");
        }
    }
}
