using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Tests.Golden;

namespace WarScript.Fuzz
{
    /// <summary>
    /// Fuzzer v0 (plan 9.6, T0.7), source mode and, with --bytecode, bytecode mode (T1.7).
    ///
    ///   WarScript.Fuzz [--minutes m] [--seed n] [--corpus folder] [--out folder]
    ///                  [--jobs n] [--cases n] [--case seed] [--replay case.ws|case.wsbc] [--bytecode]
    ///
    /// Each case takes a corpus file (default <c>Tests/Golden~</c>, without the
    /// '# isolate' files, which are known process killers), drops its comments
    /// and applies 1 to 3 random mutations (<see cref="Mutator"/>). It is
    /// written with a '# budget: 1000000' and '# memory-budget: 67108864'
    /// header and run once from source under the invariant culture through
    /// <c>WarScript.Runner</c> in a child process with a 10 s timeout. A case is
    /// a finding when the process crashed or timed out, a C# exception escaped,
    /// or an invariant was violated. Syntax, compile and script errors are
    /// acceptable outcomes.
    ///
    /// Findings are grouped by a signature (the failure with names and numbers
    /// blanked out). Each signature gets <c>&lt;out&gt;/&lt;signature hash&gt;/</c>
    /// holding the shortest case that produced it (<c>case.ws</c>), its seed,
    /// base file and mutations (<c>case.txt</c>) and the runner's JSON
    /// (<c>result.json</c>, absent when the child crashed or timed out);
    /// <c>&lt;out&gt;/summary.txt</c> lists every signature
    /// with its count and, where it matches one, the known finding or task
    /// that covers it (<see cref="KnownFindings"/>). The output folder defaults
    /// to <c>Headless~/out/fuzz/</c> (<c>Headless~/out/fuzz-bytecode/</c> with --bytecode).
    ///
    /// A case depends only on its seed and the corpus: <c>--case &lt;seed&gt;</c>
    /// regenerates and runs one case and prints it (adding or changing a
    /// corpus file changes which case a seed produces; <c>--replay
    /// &lt;case.ws&gt;</c> runs a saved case file as it is, to check a finding
    /// after a fix). The run's seed (--seed,
    /// default from the clock) is printed and written to the summary; case i's
    /// seed is derived from it, so a run with the same seed, corpus and
    /// --cases is the same set of cases whatever --jobs is.
    ///
    /// <c>--bytecode</c> is the bytecode mode (plan 9.6, T1.7): every corpus
    /// file that compiles is compiled once, in this process (the corpus is
    /// known-good source, unlike a mutated case), and each case flips 1 to 8
    /// bytes after the 9-byte header to random values or, one case in eight,
    /// truncates the file (<see cref="BytecodeMutator"/>). The case runs as a
    /// <c>.wsbc</c> file through the runner with the same budgets and
    /// timeout. Besides the outcomes above, the loader may reject it with
    /// <c>InvalidDataException</c> (outcome <c>rejected</c>); anything else
    /// escaping the load is a host fault. Imports are not compiled in: an
    /// imported file is looked up next to the case and not found. Seeds
    /// select different cases in the two modes, so <c>--case</c> needs
    /// <c>--bytecode</c> again; <c>--replay</c> recognises a <c>.wsbc</c> file.
    ///
    /// Exit codes: 0 every finding matches a known one, 4 at least one does
    /// not, 1 bad arguments or setup.
    /// </summary>
    internal static class Program
    {
        private const int CaseBudget = 1_000_000;
        private const long CaseMemoryBudget = 64L * 1024 * 1024;

        private static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var root = Metadata("WarScriptRoot");
            var minutes = 10.0;
            ulong? seed = null, single = null;
            string replay = null;
            var bytecode = false;
            var outGiven = false;
            long maxCases = 0;
            var jobs = Environment.ProcessorCount;
            var corpusDir = root == null ? null : Path.Combine(root, "Tests", "Golden~");
            var outDir = root == null ? null : Path.Combine(root, "Headless~", "out", "fuzz");

            try
            {
                for (var i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--minutes": minutes = double.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                        case "--seed": seed = ulong.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                        case "--case": single = ulong.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                        case "--replay": replay = Value(args, ref i); break;
                        case "--cases": maxCases = long.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                        case "--jobs": jobs = Math.Max(1, int.Parse(Value(args, ref i), CultureInfo.InvariantCulture)); break;
                        case "--corpus": corpusDir = Value(args, ref i); break;
                        case "--out": outDir = Value(args, ref i); outGiven = true; break;
                        case "--bytecode": bytecode = true; break;
                        default: throw new ArgumentException("unexpected argument " + args[i]);
                    }
                }
                if (corpusDir == null || outDir == null)
                    throw new ArgumentException("--corpus and --out are required when the build did not record the repository root");
                // A bytecode run keeps its own summary and cases next to the source mode's.
                if (bytecode && !outGiven) outDir += "-bytecode";
            }
            catch (System.Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine("usage: WarScript.Fuzz [--minutes m] [--seed n] [--corpus folder] [--out folder] " +
                                        "[--jobs n] [--cases n] [--case seed] [--replay case.ws|case.wsbc] [--bytecode]");
                return 1;
            }

            var runner = IsolatedRun.RunnerPath();
            if (runner == null)
            {
                Console.Error.WriteLine("WarScript.Runner is not built; build Headless~/Fuzz/WarScript.Fuzz.csproj, which builds it first");
                return 1;
            }

            if (replay != null)
            {
                var saved = IsBytecodeFile(replay)
                    ? new Case { BaseFile = replay, Bytecode = File.ReadAllBytes(replay) }
                    : new Case { BaseFile = replay, Content = File.ReadAllText(replay) };
                Execute(saved, runner, Path.GetFullPath(replay));
                Console.Out.WriteLine(saved.Describe());
                return saved.Signature == null || saved.Known != null ? 0 : 4;
            }

            var corpus = LoadCorpus(corpusDir);
            if (corpus.Count == 0)
            {
                Console.Error.WriteLine("no .ws files in " + corpusDir);
                return 1;
            }
            var compiled = bytecode ? CompileCorpus(corpus, corpusDir) : null;
            if (compiled != null && compiled.Count == 0)
            {
                Console.Error.WriteLine("no file in " + corpusDir + " compiles");
                return 1;
            }
            var mode = bytecode ? "bytecode" : "source";
            var corpusFiles = compiled?.Count ?? corpus.Count;
            Case NewCase(ulong caseSeed, string path) => compiled != null
                ? RunBytecodeCase(caseSeed, compiled, runner, Path.ChangeExtension(path, ".wsbc"))
                : RunCase(caseSeed, corpus, runner, path);

            var workDir = Path.Combine(outDir, "work");
            Directory.CreateDirectory(workDir);

            if (single.HasValue)
            {
                var one = NewCase(single.Value, Path.Combine(workDir, "case.ws"));
                Console.Out.WriteLine(one.Describe());
                if (one.Bytecode != null)
                {
                    Console.Out.WriteLine("case written to " + Path.Combine(workDir, "case.wsbc"));
                    return 0;
                }
                Console.Out.WriteLine("----- case.ws -----");
                Console.Out.WriteLine(one.Content);
                return 0;
            }

            var master = seed ?? (ulong)DateTime.UtcNow.Ticks;
            var fuzz = new Session(outDir, master, corpusDir, corpusFiles, mode);
            Console.Out.WriteLine($"fuzz: {mode} mode, seed {master}, {corpusFiles} corpus files, {jobs} jobs, " +
                                  (maxCases > 0 ? $"{maxCases} cases" : $"{minutes} minutes") + $", output in {outDir}");

            var deadline = Stopwatch.StartNew();
            var limit = TimeSpan.FromMinutes(minutes);
            long next = -1;
            var workers = Enumerable.Range(0, jobs).Select(w => new Thread(() =>
            {
                var path = Path.Combine(workDir, $"worker{w}.ws");
                while (true)
                {
                    if (maxCases <= 0 && deadline.Elapsed >= limit) return;
                    var index = Interlocked.Increment(ref next);
                    if (maxCases > 0 && index >= maxCases) return;
                    fuzz.Record(NewCase(Prng.CaseSeed(master, index), path));
                }
            }) { IsBackground = true }).ToList();
            workers.ForEach(t => t.Start());

            // FirstOrDefault: a worker can finish between the check and the pick.
            for (var alive = workers.FirstOrDefault(t => t.IsAlive); alive != null; alive = workers.FirstOrDefault(t => t.IsAlive))
            {
                alive.Join(TimeSpan.FromSeconds(30));
                Console.Out.WriteLine($"  {deadline.Elapsed:mm\\:ss}  {fuzz.Progress()}");
            }

            var summary = fuzz.WriteSummary(deadline.Elapsed);
            Console.Out.WriteLine(summary);
            return fuzz.UnknownSignatures > 0 ? 4 : 0;
        }

        private static List<(string name, string source)> LoadCorpus(string dir)
        {
            var isolate = new Regex(@"^\s*#\s*isolate\b", RegexOptions.Multiline);
            return Directory.GetFiles(dir, "*.ws", SearchOption.AllDirectories)
                .Select(p => (name: Path.GetRelativePath(dir, p).Replace('\\', '/'), source: File.ReadAllText(p)))
                .Where(f => !isolate.IsMatch(f.source))
                .OrderBy(f => f.name, StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsBytecodeFile(string path) =>
            path.EndsWith(".wsbc", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The corpus compiled as the golden runner's bytecode mode does (full
        /// stdlib registered, then <c>SaveBytecode</c>); files that do not
        /// compile are left out.
        /// </summary>
        private static List<(string name, byte[] bytes)> CompileCorpus(List<(string name, string source)> corpus, string corpusDir)
        {
            var result = new List<(string, byte[])>();
            foreach (var (name, source) in corpus)
            {
                try
                {
                    var script = new WarScriptLanguage(name, source, null, (s, m) => { });
                    global::WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);
                    var ms = new MemoryStream();
                    script.SaveBytecode(ms);
                    result.Add((name, ms.ToArray()));
                }
                catch (System.Exception)
                {
                    // A compile error (the file's golden checks it) or a known compiler crash (CRASH-6).
                }
            }
            return result;
        }

        private static Case RunBytecodeCase(ulong caseSeed, List<(string name, byte[] bytes)> corpus, string runner, string path)
        {
            var rng = new Prng(caseSeed);
            var (name, bytes) = rng.Pick(corpus);
            var c = new Case { Seed = caseSeed, BaseFile = name };
            c.Bytecode = BytecodeMutator.Mutate(bytes, rng, c.Mutations);
            File.WriteAllBytes(path, c.Bytecode);
            Execute(c, runner, path);
            return c;
        }

        private static Case RunCase(ulong caseSeed, List<(string name, string source)> corpus, string runner, string path)
        {
            var rng = new Prng(caseSeed);
            var (name, source) = rng.Pick(corpus);
            var c = new Case { Seed = caseSeed, BaseFile = name };
            var mutated = Mutator.Mutate(source, corpus.Select(f => f.source).ToList(), rng, c.Mutations);
            c.Content = $"# budget: {CaseBudget}\n# memory-budget: {CaseMemoryBudget}\n{mutated}\n";

            File.WriteAllText(path, c.Content, new UTF8Encoding(false));
            Execute(c, runner, path);
            return c;
        }

        private static void Execute(Case c, string runner, string path)
        {
            var file = new GoldenFile { Path = path, Name = Path.GetFileName(path), Source = c.Content ?? "" };
            // The budgets are also in a source case's header; a .wsbc file has no directives.
            c.Run = IsolatedRun.Execute(runner, file, GoldenMode.Source, "", CaseBudget, CaseMemoryBudget);
            c.Signature = Signature(c.Run);
            c.Known = c.Signature == null ? null : KnownFindings.Match(c.Signature);
        }

        /// <summary>The finding a run represents, or null when its outcome is acceptable.</summary>
        internal static string Signature(GoldenRun run)
        {
            if (run.Crash != null) return "crash: " + Normalize(run.Crash);
            if (run.EscapedException != null) return "host-fault: " + Normalize(run.EscapedException);
            if (run.InvariantViolation != null)
                return "invariant: " + Normalize(run.InvariantViolation) +
                       (run.ErrorMessage != null ? " (after an unhandled error)" : "");
            return null;
        }

        /// <summary>
        /// Blanks out what varies between occurrences of one failure: quoted
        /// names and numbers. Only the message is cut to its first line and 200
        /// characters; the trailing "(in &lt;method&gt;)" site is kept whole, because
        /// <see cref="KnownFindings"/> matches on it.
        /// </summary>
        internal static string Normalize(string s)
        {
            var site = SiteSuffix.Match(s);
            var suffix = site.Success ? site.Value : "";
            if (site.Success) s = s.Substring(0, site.Index);
            var end = s.IndexOf('\n');
            if (end >= 0) s = s.Substring(0, end);
            s = Regex.Replace(s, "'[^']*'", "'_'");
            s = Regex.Replace(s, "\"[^\"]*\"", "\"_\"");
            s = Regex.Replace(s, @"\d+", "N");
            s = s.Length <= 200 ? s.TrimEnd() : s.Substring(0, 200);
            return s + suffix;
        }

        /// <summary>The " (in &lt;method&gt;)" that the runner and GoldenRun append to a crash or escaped exception.</summary>
        private static readonly Regex SiteSuffix = new Regex(@" \(in [\w.+<>`]+\)$");

        private static string Metadata(string key) =>
            typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == key)?.Value;

        private static string Value(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException(args[i] + " needs a value");
            return args[++i];
        }
    }

    internal sealed class Case
    {
        public ulong? Seed;   // null: a replayed case file
        public string BaseFile;
        public readonly List<string> Mutations = new List<string>();
        public string Content;     // a source case
        public byte[] Bytecode;    // a bytecode case (--bytecode)
        public GoldenRun Run;
        public string Signature;   // null: acceptable outcome
        public string Known;       // finding/task covering the signature, or null

        /// <summary>The case's size, to keep the shortest case per signature.</summary>
        public int Size => Bytecode?.Length ?? Content.Length;

        public string FileName => Bytecode != null ? "case.wsbc" : "case.ws";

        public string Outcome =>
            Signature != null ? "finding" :
            Run.Rejected != null ? "rejected" :
            Run.CompileError != null ? "compile-error" :
            Run.ErrorMessage != null ? "script-error" : "ok";

        public string Describe()
        {
            var sb = new StringBuilder();
            if (Seed.HasValue) sb.Append("seed: ").Append(Seed.Value).Append('\n');
            sb.Append(Seed.HasValue ? "base: " : "file: ").Append(BaseFile).Append('\n');
            foreach (var m in Mutations) sb.Append("mutation: ").Append(m).Append('\n');
            sb.Append("outcome: ").Append(Outcome).Append('\n');
            if (Signature != null) sb.Append("signature: ").Append(Signature).Append('\n');
            if (Signature != null) sb.Append("known: ").Append(Known ?? "no (new)").Append('\n');
            if (Run.Crash != null) sb.Append("crash: ").Append(Run.Crash).Append('\n');
            else sb.Append("result: ").Append(IsolatedRun.ToJson(Run)).Append('\n');
            sb.Append("reproduce: WarScript.Fuzz --replay ").Append(FileName);
            if (Seed.HasValue)
                sb.Append("   (or --case ").Append(Seed.Value).Append(Bytecode != null ? " --bytecode" : "").Append(" with the same corpus)");
            sb.Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>Counts outcomes and keeps one (the shortest) case per finding signature.</summary>
    internal sealed class Session
    {
        private readonly object _lock = new object();
        private readonly string _outDir;
        private readonly ulong _seed;
        private readonly string _corpusDir;
        private readonly int _corpusFiles;
        private readonly string _mode;
        private readonly Dictionary<string, int> _outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, (int count, int length, string known)> _signatures =
            new Dictionary<string, (int, int, string)>(StringComparer.Ordinal);
        private long _cases;

        public Session(string outDir, ulong seed, string corpusDir, int corpusFiles, string mode)
        {
            _mode = mode;
            _outDir = outDir;
            _seed = seed;
            _corpusDir = corpusDir;
            _corpusFiles = corpusFiles;
        }

        public int UnknownSignatures
        {
            get { lock (_lock) return _signatures.Values.Count(s => s.known == null); }
        }

        public void Record(Case c)
        {
            lock (_lock)
            {
                _cases++;
                _outcomes[c.Outcome] = (_outcomes.TryGetValue(c.Outcome, out var n) ? n : 0) + 1;
                if (c.Signature == null) return;

                var shorter = !_signatures.TryGetValue(c.Signature, out var entry) || c.Size < entry.length;
                _signatures[c.Signature] = (entry.count + 1, shorter ? c.Size : entry.length, c.Known);
                if (!shorter) return;

                var dir = Path.Combine(_outDir, Hash(c.Signature));
                Directory.CreateDirectory(dir);
                if (c.Bytecode != null) File.WriteAllBytes(Path.Combine(dir, c.FileName), c.Bytecode);
                else File.WriteAllText(Path.Combine(dir, c.FileName), c.Content, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(dir, "case.txt"), c.Describe(), new UTF8Encoding(false));
                // A crashed or timed-out child wrote no JSON; case.txt has the crash.
                if (c.Run.Crash == null)
                    File.WriteAllText(Path.Combine(dir, "result.json"), IsolatedRun.ToJson(c.Run) + "\n", new UTF8Encoding(false));
            }
        }

        public string Progress()
        {
            lock (_lock)
                return $"{_cases} cases, " +
                       string.Join(", ", _outcomes.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => $"{o.Key} {o.Value}")) +
                       $"; {_signatures.Count} finding signatures ({_signatures.Values.Count(s => s.known == null)} new)";
        }

        public string WriteSummary(TimeSpan elapsed)
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                sb.Append("mode: ").Append(_mode).Append('\n');
                sb.Append("seed: ").Append(_seed).Append('\n');
                sb.Append("corpus: ").Append(_corpusDir).Append(" (").Append(_corpusFiles).Append(" files)\n");
                sb.Append("elapsed: ").Append(elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("cases: ").Append(_cases).Append('\n');
                foreach (var o in _outcomes.OrderBy(o => o.Key, StringComparer.Ordinal))
                    sb.Append("  ").Append(o.Key).Append(": ").Append(o.Value).Append('\n');
                sb.Append("finding signatures: ").Append(_signatures.Count).Append('\n');
                foreach (var s in _signatures.OrderByDescending(s => s.Value.count).ThenBy(s => s.Key, StringComparer.Ordinal))
                {
                    sb.Append("  ").Append(Hash(s.Key)).Append("  ").Append(s.Value.count.ToString(CultureInfo.InvariantCulture).PadLeft(6))
                      .Append("  ").Append(s.Value.known ?? "NEW").Append("  ").Append(s.Key).Append('\n');
                }
                var text = sb.ToString();
                File.WriteAllText(Path.Combine(_outDir, "summary.txt"), text, new UTF8Encoding(false));
                return text;
            }
        }

        /// <summary>FNV-1a 64 of the signature, as the name of its folder.</summary>
        private static string Hash(string signature)
        {
            var h = 0xcbf29ce484222325UL;
            foreach (var b in Encoding.UTF8.GetBytes(signature))
            {
                h ^= b;
                h *= 0x100000001b3UL;
            }
            return h.ToString("x16", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Finding signatures that are already known, with the finding or task
    /// that covers them (plan section 4 and Appendix E). A signature that
    /// matches none is reported as NEW and makes the run exit with code 4.
    /// Remove an entry when its fix lands, so a regression shows up as NEW.
    /// </summary>
    internal static class KnownFindings
    {
        private static readonly (Regex pattern, string id)[] Table =
        {
            // Thrown while parsing or compiling (the site is a frontend method), not while running.
            (new Regex(@"^host-fault: \w*Exception: .*\(in WarScript\.(Parser\.|Expression\.ExpressionReader\.|Expression\.Operator\.Extensions\.|Bytecode\.Compiler\.)[\w.]+\)$"), "CRASH-6"),
            // Since BUDGET-2 was fixed (T1.5) the only hang seen is the state hash
            // walking shared arrays a mutated A.27 leaves in globals (BUDGET-3,
            // state hash part, Q-13); any other cause looks the same, so check a
            // timeout's case.
            (new Regex(@"^crash: timed out"), "BUDGET-3?"),
            // A yield outside a coroutine in a called frame ends Run() with that
            // frame's scopes pushed (SEM-14, Q-15; the bytecode mode reaches it
            // by turning a Return into YieldWait). Any other scope leak after a
            // completed run looks the same, so check the case.
            (new Regex(@"^invariant: MemoryContextDepth: N before the entry, N after$"), "SEM-14?"),
        };

        public static string Match(string signature)
        {
            foreach (var (pattern, id) in Table)
                if (pattern.IsMatch(signature)) return id;
            return null;
        }
    }
}
