#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WarScript.Audit
{
    /// <summary>
    /// Collects <see cref="AuditEvent"/>s and writes them as Markdown or JSON
    /// (plan 8.1, T0.8). Events are de-duplicated by (kind, script, line):
    /// each site keeps its count, the functions it was seen in and the first
    /// event's detail. Thread-safe, so one report can collect from several
    /// instances.
    ///
    /// <code>
    ///   var report = new AuditReport();
    ///   script.AuditSink = report.Add;      // for every instance to audit
    ///   ...                                 // play
    ///   File.WriteAllText("audit.md", report.ToMarkdown("Game session"));
    /// </code>
    /// </summary>
    public sealed class AuditReport
    {
        /// <summary>One (kind, script, line) site.</summary>
        public sealed class Entry
        {
            public AuditKind Kind { get; }
            public string Script { get; }
            public int Line { get; }
            public long Count { get; internal set; }
            /// <summary>The detail of the first event seen at this site.</summary>
            public string FirstDetail { get; }
            internal readonly SortedSet<string> FunctionSet = new SortedSet<string>(StringComparer.Ordinal);
            /// <summary>The WarScript functions the site's events happened in, sorted.</summary>
            public IReadOnlyCollection<string> Functions => FunctionSet;

            internal Entry(AuditKind kind, string script, int line, string firstDetail)
            {
                Kind = kind;
                Script = script;
                Line = line;
                FirstDetail = firstDetail;
            }
        }

        private readonly object _lock = new object();
        private readonly Dictionary<(AuditKind, string, int), Entry> _entries =
            new Dictionary<(AuditKind, string, int), Entry>();
        private long _events;

        /// <summary>Records one event. Usable directly as a sink: <c>script.AuditSink = report.Add</c>.</summary>
        public void Add(AuditEvent e)
        {
            lock (_lock)
            {
                _events++;
                var key = (e.Kind, e.Script ?? "", e.Line);
                if (!_entries.TryGetValue(key, out var entry))
                {
                    entry = new Entry(e.Kind, key.Item2, e.Line, e.Detail ?? "");
                    _entries.Add(key, entry);
                }
                entry.Count++;
                if (e.Function != null) entry.FunctionSet.Add(e.Function);
            }
        }

        /// <summary>Total number of events recorded.</summary>
        public long EventCount
        {
            get { lock (_lock) return _events; }
        }

        /// <summary>The sites, ordered by kind, script (ordinal) and line.</summary>
        public IReadOnlyList<Entry> Entries
        {
            get
            {
                lock (_lock)
                    return _entries.Values
                        .OrderBy(e => e.Kind)
                        .ThenBy(e => e.Script, StringComparer.Ordinal)
                        .ThenBy(e => e.Line)
                        .ToList();
            }
        }

        /// <summary>
        /// A Markdown report: a summary table with one row per kind, then one
        /// section per kind that occurred, listing its sites.
        /// </summary>
        public string ToMarkdown(string title = "WarScript audit report")
        {
            var entries = Entries;
            var sb = new StringBuilder();
            sb.Append("# ").Append(title).Append("\n\n");
            sb.Append("Events: ").Append(Num(EventCount)).Append(", sites (kind, script, line): ")
              .Append(Num(entries.Count)).Append(".\n\n");

            sb.Append("| Kind | Sites | Events |\n|---|---:|---:|\n");
            foreach (AuditKind kind in Enum.GetValues(typeof(AuditKind)))
            {
                var ofKind = entries.Where(e => e.Kind == kind).ToList();
                sb.Append("| `").Append(kind).Append("` | ").Append(Num(ofKind.Count)).Append(" | ")
                  .Append(Num(ofKind.Sum(e => e.Count))).Append(" |\n");
            }

            foreach (var group in entries.GroupBy(e => e.Kind))
            {
                sb.Append("\n## ").Append(group.Key).Append("\n\n");
                sb.Append("| Site | Function | Events | First seen |\n|---|---|---:|---|\n");
                foreach (var e in group)
                {
                    sb.Append("| ").Append(Cell(e.Script)).Append(':').Append(Num(e.Line))
                      .Append(" | ").Append(Cell(string.Join(", ", e.Functions)))
                      .Append(" | ").Append(Num(e.Count))
                      .Append(" | ").Append(Cell(e.FirstDetail)).Append(" |\n");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// The report as JSON: <c>{"events": n, "sites": [{"kind", "script",
        /// "line", "count", "functions": [...], "first": "..."}]}</c>.
        /// </summary>
        public string ToJson()
        {
            var entries = Entries;
            var sb = new StringBuilder();
            sb.Append("{\"events\": ").Append(Num(EventCount)).Append(", \"sites\": [");
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                sb.Append(i == 0 ? "\n  " : ",\n  ");
                sb.Append("{\"kind\": ").Append(Json(e.Kind.ToString()))
                  .Append(", \"script\": ").Append(Json(e.Script))
                  .Append(", \"line\": ").Append(Num(e.Line))
                  .Append(", \"count\": ").Append(Num(e.Count))
                  .Append(", \"functions\": [").Append(string.Join(", ", e.Functions.Select(Json))).Append(']')
                  .Append(", \"first\": ").Append(Json(e.FirstDetail)).Append('}');
            }
            sb.Append(entries.Count == 0 ? "]}\n" : "\n]}\n");
            return sb.ToString();
        }

        private static string Num(long n) => n.ToString(CultureInfo.InvariantCulture);

        private static string Cell(string s) =>
            s.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r", "").Replace("\n", "\\n");

        private static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
