// WarScript.Migrate: the one-time v1 -> v2 migration tool (T3.2, plan 8.2).
//
//   dotnet run --project "Tools~/WarScript.Migrate" -- (--report | --rewrite) <root>...
//       [--out <dir>] [--report-file <file>] [--host-globals <a,b,...>] [--host-globals-file <file>]
//
// <root> is a .ws file or a folder (searched recursively for *.ws). Every
// file is parsed with the v1 frontend; files connected by imports form a
// module set and are analysed together. --report writes the report only;
// --rewrite also rewrites the files: in place, or under --out (keeping
// their paths relative to the root they were found under). The report goes
// to --report-file, by default migration-report.md in --out or in the
// current folder. Host globals ([WsConst]/[WsEnum] names and anything the
// host sets) are given by name, or one per line in a file ('#' comments).
//
// Exit code: 0 done (with or without manual items), 1 bad arguments,
// 2 a file did not parse (it is listed in the report and left unchanged).

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
            bool? rewrite = null;
            string outDir = null, reportFile = null;
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
                        case "--host-globals":
                            hostGlobals.AddRange(args[++i].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
                            break;
                        case "--host-globals-file":
                            hostGlobals.AddRange(File.ReadAllLines(args[++i])
                                .Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0));
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
            catch (System.Exception e) when (e is ArgumentException || e is IndexOutOfRangeException || e is IOException)
            {
                Console.Error.WriteLine("WarScript.Migrate: " + e.Message);
                Console.Error.WriteLine("usage: WarScript.Migrate (--report | --rewrite) <root>... [--out <dir>] [--report-file <file>] [--host-globals <a,b>] [--host-globals-file <file>]");
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

            var result = new Migrator(hostGlobals).Run(inputs.Select(x => x.File).ToList());

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

        /// <summary>A file, shown in the report as the root it was found under (as given) plus its path in it.</summary>
        private static MigrationFile Read(string fullPath, string shown) =>
            new MigrationFile(shown, fullPath, File.ReadAllText(fullPath));

        private static string Shown(string root, string relative) =>
            (root.TrimEnd('/', '\\') + "/" + relative).Replace('\\', '/');
    }
}
