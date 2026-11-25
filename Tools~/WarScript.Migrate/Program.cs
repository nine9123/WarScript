// WarScript.Migrate: the one-time v1 -> v2 migration tool (T3.2, plan 8.2).
//
//   dotnet run --project "Tools~/WarScript.Migrate" -- (--report | --rewrite) <root>...
//       [--out <dir>] [--report-file <file>] [--host-globals <a,b,...>] [--host-globals-file <file>]
//       [--manifest <file>] [--import-root <dir>]
//
// <root> is a .ws file or a folder (searched recursively for *.ws). Every
// file is parsed with the v1 frontend; files connected by imports form a
// module set and are analysed together. --report writes the report only;
// --rewrite also rewrites the files: in place, or under --out (keeping
// their paths relative to the root they were found under). The report goes
// to --report-file, by default migration-report.md in --out or in the
// current folder. Host globals ([WsConst]/[WsEnum] names and anything the
// host sets) are given by name, or one per line in a file ('#' comments),
// or read from a native manifest (WarScriptNatives.wsmanifest, written by
// Generate Bindings: its globals, constants and enums; T3.8, PROP-30).
// Imports are resolved against the importing file's folder, or against
// --import-root when given (a mod names its imports from its root; T3.9).
//
// Exit code: 0 done (with or without manual items), 1 bad arguments,
// 2 a file did not parse (it is listed in the report and left unchanged).
//
//   dotnet run --project "Tools~/WarScript.Migrate" -- --syntax <root>... [--out <dir>] [--ext <.ws,...>]
//
// Rewrites scripts from the legacy syntax (f [a], {1, 2}, a{i}, obj :: x)
// to the current one (f(a), [1, 2], a[i], obj.x) (T3.12; SyntaxRewriter.cs):
// in place, or under --out. Run it after --rewrite when migrating v1 scripts.
// Files are *.ws unless --ext lists other extensions. It prints each file
// it changed and the raw text literals ("""...""") it left alone, which may
// hold script source to rewrite by hand. Exit code: 0 done, 1 bad
// arguments, 2 a file did not lex (it is listed and left unchanged).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WarScript.Migrate
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--syntax")
                return Syntax(args);

            bool? rewrite = null;
            string outDir = null, reportFile = null, importRoot = null;
            var roots = new List<string>();
            var hostGlobals = new List<string>();
            try
            {
                for (var i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--report": rewrite = false; break;
                        case "--rewrite": rewrite = true; break;
                        case "--out": outDir = args[++i]; break;
                        case "--report-file": reportFile = args[++i]; break;
                        case "--import-root": importRoot = Path.GetFullPath(args[++i]); break;
                        case "--host-globals":
                            hostGlobals.AddRange(args[++i].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
                            break;
                        case "--host-globals-file":
                            hostGlobals.AddRange(File.ReadAllLines(args[++i])
                                .Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0));
                            break;
                        case "--manifest":
                            hostGlobals.AddRange(Migrator.HostGlobals(NativeManifest.Parse(File.ReadAllText(args[++i]))));
                            break;
                        default:
                            if (args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("unknown option " + args[i]);
                            roots.Add(args[i]);
                            break;
                    }
                }
                if (rewrite == null) throw new ArgumentException("give --report or --rewrite");
                if (roots.Count == 0) throw new ArgumentException("give at least one root");
            }
            catch (System.Exception e) when (e is ArgumentException || e is IndexOutOfRangeException || e is IOException || e is FormatException)
            {
                Console.Error.WriteLine("WarScript.Migrate: " + e.Message);
                Console.Error.WriteLine("usage: WarScript.Migrate (--report | --rewrite) <root>... [--out <dir>] [--report-file <file>] [--host-globals <a,b>] [--host-globals-file <file>] [--manifest <file>] [--import-root <dir>]");
                return 1;
            }

            // (file, root it was found under)
            var inputs = new List<(MigrationFile File, string Root)>();
            foreach (var root in roots)
            {
                var full = Path.GetFullPath(root);
                if (File.Exists(full))
                {
                    inputs.Add((Read(full, root.Replace('\\', '/')), Path.GetDirectoryName(full)));
                }
                else if (Directory.Exists(full))
                {
                    foreach (var f in Directory.EnumerateFiles(full, "*.ws", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                        inputs.Add((Read(f, Shown(root, Path.GetRelativePath(full, f))), full));
                }
                else
                {
                    Console.Error.WriteLine("WarScript.Migrate: no such file or folder: " + root);
                    return 1;
                }
            }

            var result = new Migrator(hostGlobals) { ImportRoot = importRoot }.Run(inputs.Select(x => x.File).ToList());

            if (rewrite.Value)
            {
                foreach (var (file, root) in inputs)
                {
                    if (!result.Rewritten.TryGetValue(file.Path, out var source)) continue;
                    var target = outDir == null
                        ? file.FullPath
                        : Path.Combine(Path.GetFullPath(outDir), Path.GetRelativePath(root, file.FullPath));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllText(target, source, new UTF8Encoding(false));
                }
            }

            reportFile ??= Path.Combine(outDir ?? ".", "migration-report.md");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile)));
            File.WriteAllText(reportFile, Report.Markdown(result, roots.Select(r => r.Replace('\\', '/')), rewrite.Value), new UTF8Encoding(false));

            var manual = result.Entries.Count(e => e.Kind == EntryKind.Manual);
            Console.WriteLine(FormattableString.Invariant(
                $"{result.FileCount} files, {result.Rewritten.Count} {(rewrite.Value ? "rewritten" : "with rewrites")}, {result.Entries.Count} entries ({manual} manual); report: {reportFile}"));
            return result.Entries.Any(e => e.Rule == Rules.ParseError) ? 2 : 0;
        }

        /// <summary>The <c>--syntax</c> mode: legacy syntax to current syntax (T3.12).</summary>
        private static int Syntax(string[] args)
        {
            string outDir = null;
            var extensions = new List<string> { ".ws" };
            var roots = new List<string>();
            try
            {
                for (var i = 1; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--out": outDir = args[++i]; break;
                        case "--ext":
                            extensions = args[++i].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                            break;
                        default:
                            if (args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("unknown option " + args[i]);
                            roots.Add(args[i]);
                            break;
                    }
                }
                if (roots.Count == 0) throw new ArgumentException("give at least one root");
            }
            catch (System.Exception e) when (e is ArgumentException || e is IndexOutOfRangeException)
            {
                Console.Error.WriteLine("WarScript.Migrate: " + e.Message);
                Console.Error.WriteLine("usage: WarScript.Migrate --syntax <root>... [--out <dir>] [--ext <.ws,...>]");
                return 1;
            }

            var files = new List<(string Path, string Root)>();
            foreach (var root in roots)
            {
                var full = Path.GetFullPath(root);
                if (File.Exists(full))
                    files.Add((full, Path.GetDirectoryName(full)));
                else if (Directory.Exists(full))
                    files.AddRange(Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                        .Where(f => extensions.Contains(Path.GetExtension(f)))
                        .OrderBy(f => f, StringComparer.Ordinal)
                        .Select(f => (f, full)));
                else
                {
                    Console.Error.WriteLine("WarScript.Migrate: no such file or folder: " + root);
                    return 1;
                }
            }

            int changed = 0, failed = 0, raw = 0;
            foreach (var (path, root) in files)
            {
                var shown = Path.GetRelativePath(Directory.GetCurrentDirectory(), path).Replace('\\', '/');
                SyntaxRewriter.Result result;
                try
                {
                    result = SyntaxRewriter.Rewrite(File.ReadAllText(path));
                }
                catch (WarScript.Exception.SyntaxException e)
                {
                    Console.WriteLine(shown + ": not rewritten: " + e.Message);
                    failed++;
                    continue;
                }
                foreach (var line in result.RawLiteralLines)
                {
                    Console.WriteLine(FormattableString.Invariant($"{shown}:{line}: raw text literal left as written (rewrite any script source in it by hand)"));
                    raw++;
                }
                var target = outDir == null ? path : Path.Combine(Path.GetFullPath(outDir), Path.GetRelativePath(root, path));
                if (result.Edits == 0 && outDir == null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllText(target, result.Source, new UTF8Encoding(false));
                if (result.Edits > 0)
                {
                    Console.WriteLine(FormattableString.Invariant($"{shown}: {result.Edits} tokens rewritten"));
                    changed++;
                }
            }
            Console.WriteLine(FormattableString.Invariant(
                $"{files.Count} files, {changed} rewritten, {raw} raw literals to check, {failed} not lexed"));
            return failed > 0 ? 2 : 0;
        }

        /// <summary>A file, shown in the report as the root it was found under (as given) plus its path in it.</summary>
        private static MigrationFile Read(string fullPath, string shown) =>
            new MigrationFile(shown, fullPath, File.ReadAllText(fullPath));

        private static string Shown(string root, string relative) =>
            (root.TrimEnd('/', '\\') + "/" + relative).Replace('\\', '/');
    }
}
