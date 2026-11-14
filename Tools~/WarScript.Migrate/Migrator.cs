#nullable enable

// The v1 → v2 migration analysis and rewriter (T3.2, plan 8.2). NUnit-free
// and file-system-free: Program.cs reads the files and writes the results;
// Headless~/Tests compiles this file too (Tools~/WarScript.Migrate/Tests).
// Temporary: delete with the tool once every script is migrated (plan 8.3).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using WarScript.Context.Definition;
using WarScript.Exception;
using WarScript.Expression;
using WarScript.Expression.Operator;
using WarScript.Expression.Value;
using WarScript.Statement;
using WarScript.Statement.Loop;
using WarScript.Token;

namespace WarScript.Migrate
{
    /// <summary>One script handed to the migrator.</summary>
    internal sealed class MigrationFile
    {
        /// <summary>The path shown in the report ('/' separators).</summary>
        public readonly string Path;

        /// <summary>The full path, for import resolution.</summary>
        public readonly string FullPath;

        public readonly string Source;

        public MigrationFile(string path, string fullPath, string source)
        {
            Path = path;
            FullPath = fullPath;
            Source = source;
        }
    }

    /// <summary>What a report entry asks for.</summary>
    internal enum EntryKind
    {
        /// <summary>The tool rewrites the source (in --rewrite mode).</summary>
        Rewrite,
        /// <summary>A human has to decide or change something.</summary>
        Manual,
    }

    /// <summary>One item of the report: <c>file:line:col</c>, source line, rule, action (plan 8.2).</summary>
    internal sealed class ReportEntry
    {
        public readonly string File;
        public readonly int Line;
        public readonly int Column;
        public readonly string SourceLine;
        public readonly string Rule;
        public EntryKind Kind;
        public string Action;

        public ReportEntry(string file, int line, int column, string sourceLine, string rule, EntryKind kind, string action)
        {
            File = file;
            Line = line;
            Column = column;
            SourceLine = sourceLine;
            Rule = rule;
            Kind = kind;
            Action = action;
        }

        public string Location => FormattableString.Invariant($"{File}:{Line}:{Column}");
    }

    /// <summary>The migrator's answer: rewritten sources and the report entries.</summary>
    internal sealed class MigrationResult
    {
        /// <summary>The new source of every file the rules changed, by <see cref="MigrationFile.Path"/>.</summary>
        public readonly SortedDictionary<string, string> Rewritten = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Every entry, sorted by file, line, column and rule.</summary>
        public readonly List<ReportEntry> Entries = new List<ReportEntry>();

        public int FileCount;
    }

    /// <summary>
    /// The rule identifiers used in the report, one per row of the table in
    /// plan 8.2 plus the tool's own diagnostics.
    /// </summary>
    internal static class Rules
    {
        public const string GlobalWrite = "global-write";
        public const string FieldAccess = "field-access";
        public const string MethodWritesGlobal = "method-writes-global";
        public const string MethodReadsGlobal = "method-reads-global";
        public const string ImportOrder = "import-order";
        public const string GlobalName = "global-name";
        public const string TextSubtraction = "text-subtraction";
        public const string BlockEscape = "block-escape";
        public const string DynamicProperty = "dynamic-property";
        public const string TextIndex = "text-index";
        public const string ParseError = "parse-error";
        public const string UnresolvedImport = "import-unresolved";

        /// <summary>Report order and the plan 8.2 wording of each rule.</summary>
        public static readonly (string Id, string Description)[] All =
        {
            (GlobalWrite, "A function or lambda writes a global"),
            (FieldAccess, "A method uses fields by bare name"),
            (MethodWritesGlobal, "A method writes a name that is a global"),
            (MethodReadsGlobal, "A method or class body reads a name that is a global"),
            (ImportOrder, "An import comes after other statements"),
            (GlobalName, "A variable is named `global`"),
            (TextSubtraction, "Text subtraction with a literal"),
            (BlockEscape, "A top-level block variable escapes its block"),
            (DynamicProperty, "A dynamic property is created"),
            (TextIndex, "A text index is assigned"),
            (ParseError, "The file does not parse with the v1 frontend"),
            (UnresolvedImport, "An import names no file among the inputs"),
        };
    }

    /// <summary>
    /// Parses every file with the v1 frontend (plus the T3.1 columns),
    /// builds the symbol tables of each module set and applies the rules of
    /// plan 8.2. A module set is a group of files connected by imports
    /// (in either direction): a file imported by several entry scripts is
    /// analysed against all of them, which keeps every v1 global write of
    /// every program it is part of.
    /// </summary>
    internal sealed class Migrator
    {
        private readonly HashSet<string> _hostGlobals;

        /// <param name="hostGlobals">
        /// Names the host provides (<c>[WsConst]</c>/<c>[WsEnum]</c> names and
        /// anything the host sets): writes to them are global writes.
        /// </param>
        public Migrator(IEnumerable<string> hostGlobals)
        {
            _hostGlobals = new HashSet<string>(hostGlobals, StringComparer.Ordinal);
        }

        public MigrationResult Run(IReadOnlyList<MigrationFile> files)
        {
            var result = new MigrationResult { FileCount = files.Count };
            var parsed = new List<ParsedFile>();
            foreach (var file in files.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                var p = new ParsedFile(file);
                if (p.Parse(out var error))
                {
                    parsed.Add(p);
                }
                else
                {
                    result.Entries.Add(new ReportEntry(file.Path, error!.Line, error.Column,
                        p.SourceLineOf(error.Line), Rules.ParseError, EntryKind.Manual,
                        "Not migrated: " + error.Message));
                }
            }

            ResolveImports(parsed, result);
            foreach (var set in ModuleSets(parsed))
                new ModuleSet(set, _hostGlobals).Analyse();

            foreach (var file in parsed)
            {
                result.Entries.AddRange(file.Entries);
                var rewritten = file.Edits.Apply(file.Source);
                if (rewritten != file.Source)
                    result.Rewritten[file.File.Path] = rewritten;
            }

            result.Entries.Sort((a, b) =>
            {
                var c = string.CompareOrdinal(a.File, b.File);
                if (c == 0) c = a.Line.CompareTo(b.Line);
                if (c == 0) c = a.Column.CompareTo(b.Column);
                if (c == 0) c = string.CompareOrdinal(a.Rule, b.Rule);
                return c;
            });
            return result;
        }

        private static void ResolveImports(List<ParsedFile> files, MigrationResult result)
        {
            var byPath = new Dictionary<string, ParsedFile>(StringComparer.Ordinal);
            foreach (var f in files) byPath[Normalize(f.File.FullPath)] = f;

            foreach (var file in files)
            {
                var directory = System.IO.Path.GetDirectoryName(file.File.FullPath) ?? "";
                foreach (var import in file.ImportStatements)
                {
                    var candidate = Normalize(System.IO.Path.Combine(directory, import.Path));
                    if (!byPath.TryGetValue(candidate, out var target)
                        && !byPath.TryGetValue(candidate + ".ws", out target))
                    {
                        file.Entries.Add(file.Entry(import.RowNumber ?? 0, import.Column, Rules.UnresolvedImport, EntryKind.Manual,
                            $"`import \"{import.Path}\"` names no file among the inputs: its globals and classes are not part of the analysis. Add the file (or its root) to the inputs."));
                        continue;
                    }
                    file.Imports.Add(target);
                }
            }
        }

        private static string Normalize(string path) =>
            System.IO.Path.GetFullPath(path).Replace('\\', '/');

        /// <summary>The connected components of the import graph, in file order.</summary>
        private static List<List<ParsedFile>> ModuleSets(List<ParsedFile> files)
        {
            var index = new Dictionary<ParsedFile, int>();
            for (var i = 0; i < files.Count; i++) index[files[i]] = i;
            var parent = Enumerable.Range(0, files.Count).ToArray();
            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
            foreach (var f in files)
                foreach (var target in f.Imports)
                    parent[Find(index[f])] = Find(index[target]);

            var sets = new SortedDictionary<int, List<ParsedFile>>();
            for (var i = 0; i < files.Count; i++)
            {
                var root = Find(i);
                if (!sets.TryGetValue(root, out var list)) sets[root] = list = new List<ParsedFile>();
                list.Add(files[i]);
            }
            return sets.Values.ToList();
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  One parsed file
    // ────────────────────────────────────────────────────────────────────

    internal sealed class ParsedFile
    {
        public readonly MigrationFile File;
        public string Source => File.Source;
        public IReadOnlyList<Token.Token> Tokens = Array.Empty<Token.Token>();
        public CompositeStatement TopLevel = null!;
        public DefinitionScope Definitions = null!;
        public readonly Dictionary<IExpression, ExpressionSpan> Spans =
            new Dictionary<IExpression, ExpressionSpan>(ReferenceComparer.Instance);
        public readonly List<ImportStatement> ImportStatements = new List<ImportStatement>();
        public readonly List<ParsedFile> Imports = new List<ParsedFile>();
        public readonly List<ReportEntry> Entries = new List<ReportEntry>();
        public readonly EditList Edits = new EditList();

        private readonly List<int> _lineStarts = new List<int>();
        private readonly Dictionary<(int, int), int> _tokenAt = new Dictionary<(int, int), int>();

        public ParsedFile(MigrationFile file)
        {
            File = file;
            // Lines as the lexer counts them: "\r\n", "\n" and a lone "\r" each end one.
            _lineStarts.Add(0);
            var s = file.Source;
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '\n' || (s[i] == '\r' && (i + 1 >= s.Length || s[i + 1] != '\n')))
                    _lineStarts.Add(i + 1);
            }
        }

        public bool Parse(out SyntaxException? error)
        {
            error = null;
            // A fresh instance per file: no natives (the parser does not need
            // them), no resolvers (imports are not run, only read).
            var script = new WarScriptLanguage(File.Path, File.Source, null, (_, _) => { });
            try
            {
                var (tokens, topLevel, definitions) = script.ParseForMigration(Spans);
                Tokens = tokens;
                TopLevel = topLevel;
                Definitions = definitions;
            }
            catch (SyntaxException e)
            {
                error = e;
                return false;
            }

            for (var i = 0; i < Tokens.Count; i++)
            {
                var t = Tokens[i];
                if (IsEmpty(t)) continue;
                var key = (t.RowNumber, t.Column);
                if (!_tokenAt.ContainsKey(key)) _tokenAt[key] = i;
            }
            CollectImports(TopLevel);
            return true;
        }

        private void CollectImports(CompositeStatement block)
        {
            foreach (var s in Walk.Statements(block))
                if (s is ImportStatement import) ImportStatements.Add(import);
        }

        public static bool IsEmpty(Token.Token t) => t.Type == TokenType.LineBreak || t.Type == TokenType.Comment;

        /// <summary>Source offset of a (line, column), both from 1; -1 when outside the source.</summary>
        public int Offset(int line, int column)
        {
            if (line < 1 || line > _lineStarts.Count || column < 1) return -1;
            var offset = _lineStarts[line - 1] + column - 1;
            return offset <= Source.Length ? offset : -1;
        }

        public int LineStart(int line) => _lineStarts[line - 1];

        /// <summary>The offset where the line after <paramref name="line"/> starts, or the length.</summary>
        public int NextLineStart(int line) => line < _lineStarts.Count ? _lineStarts[line] : Source.Length;

        public string SourceLineOf(int line)
        {
            if (line < 1 || line > _lineStarts.Count) return "";
            var start = _lineStarts[line - 1];
            var end = start;
            while (end < Source.Length && Source[end] != '\r' && Source[end] != '\n') end++;
            return Source.Substring(start, end - start);
        }

        /// <summary>The index of the (non-empty) token at a position, or -1.</summary>
        public int TokenIndexAt(int line, int column) =>
            _tokenAt.TryGetValue((line, column), out var i) ? i : -1;

        /// <summary>The index of the next non-empty token after <paramref name="index"/>, or -1.</summary>
        public int NextToken(int index)
        {
            for (var i = index + 1; i < Tokens.Count; i++)
                if (!IsEmpty(Tokens[i])) return i;
            return -1;
        }

        public int TokenOffset(int index) => Offset(Tokens[index].RowNumber, Tokens[index].Column);

        /// <summary>
        /// The source range [start, end) of an expression's tokens, or null
        /// when it is unknown (no span was recorded, or its tokens are inside
        /// an interpolation, where the desugared tokens carry the hole's
        /// column, so the range cannot be read off the positions).
        /// </summary>
        public (int Start, int End)? SourceRange(IExpression e)
        {
            if (!Spans.TryGetValue(e, out var span)) return null;
            var start = TokenOffset(span.First);
            var lastStart = TokenOffset(span.Last);
            if (start < 0 || lastStart < start) return null;
            // A literal that starts with a hole ("{a}b") starts with the
            // '(' desugared at its '{': the literal starts at its quote.
            var first = Tokens[span.First];
            if (first.Type == TokenType.Operator && first.Value == "(" && Source[start] == '{')
            {
                if (start == 0 || Source[start - 1] != '"') return null;
                start--;
                if (start > 0 && Source[start - 1] == '$') start--;
            }
            // The expression ends where the next token (empty ones included)
            // starts, less the whitespace before it.
            var end = span.Last + 1 < Tokens.Count ? TokenOffset(span.Last + 1) : Source.Length;
            if (end < 0) return null;
            while (end > lastStart && char.IsWhiteSpace(Source[end - 1])) end--;
            if (end <= lastStart) return null;
            // Every token of the span must lie inside the range: true unless
            // an interpolation's desugared tokens are among them at an edge.
            for (var i = span.First; i <= span.Last; i++)
            {
                var o = TokenOffset(i);
                if (o < start || o >= end) return null;
            }
            return (start, end);
        }

        public ReportEntry Entry(int line, int column, string rule, EntryKind kind, string action) =>
            new ReportEntry(File.Path, line, column, SourceLineOf(line), rule, kind, action);

        /// <summary>The file's newline: "\r\n" when the file uses it, else "\n".</summary>
        public string NewLine => Source.Contains("\r\n") ? "\r\n" : "\n";
    }

    /// <summary>Reference equality for expression nodes (some override Equals).</summary>
    internal sealed class ReferenceComparer : IEqualityComparer<IExpression>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();
        public bool Equals(IExpression? x, IExpression? y) => ReferenceEquals(x, y);
        public int GetHashCode(IExpression obj) => RuntimeHelpers.GetHashCode(obj);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Source edits
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Non-overlapping edits of one file. An edit replaces [Start, End) (an
    /// insertion when they are equal). Insertions at one offset are ordered:
    /// closing text first (the shorter span's first), then opening text
    /// (the longer span's first), then in the order they were added; a
    /// replacement comes after the insertions at its start.
    /// </summary>
    internal sealed class EditList
    {
        private readonly List<Edit> _edits = new List<Edit>();
        private int _sequence;

        internal readonly struct Edit
        {
            public readonly int Start;
            public readonly int End;
            public readonly string Text;
            /// <summary>0 closing insertion, 1 opening insertion, 2 replacement.</summary>
            public readonly int Class;
            /// <summary>The sort key within the class (span length: ascending for closings, descending for openings).</summary>
            public readonly int Key;
            public readonly int Sequence;

            public Edit(int start, int end, string text, int @class, int key, int sequence)
            {
                Start = start;
                End = end;
                Text = text;
                Class = @class;
                Key = key;
                Sequence = sequence;
            }
        }

        /// <summary>Builds a group of edits that is added only if none of them conflicts.</summary>
        internal sealed class Group
        {
            internal readonly List<(int Start, int End, string Text, int Class, int Key)> Items = new List<(int, int, string, int, int)>();

            /// <summary>Insert text that opens a span of <paramref name="spanLength"/> characters (or stands alone: 0).</summary>
            public Group Open(int at, string text, int spanLength = 0) { Items.Add((at, at, text, 1, -spanLength)); return this; }

            /// <summary>Insert text that closes a span of <paramref name="spanLength"/> characters.</summary>
            public Group Close(int at, string text, int spanLength) { Items.Add((at, at, text, 0, spanLength)); return this; }

            public Group Replace(int start, int end, string text) { Items.Add((start, end, text, 2, 0)); return this; }
        }

        public bool IsEmpty => _edits.Count == 0;

        /// <summary>Adds the group unless an edit of it overlaps an accepted replacement (or vice versa).</summary>
        public bool TryAdd(Group group)
        {
            foreach (var (start, end, _, _, _) in group.Items)
            {
                foreach (var e in _edits)
                {
                    if (start < end && e.Start < e.End && start < e.End && e.Start < end) return false; // two replacements overlap
                    if (start < end && e.Start == e.End && e.Start > start && e.Start < end) return false; // insertion inside the new replacement
                    if (e.Start < e.End && start == end && start > e.Start && start < e.End) return false; // new insertion inside a replacement
                }
            }
            foreach (var (start, end, text, @class, key) in group.Items)
                _edits.Add(new Edit(start, end, text, @class, key, _sequence++));
            return true;
        }

        public string Apply(string source)
        {
            if (_edits.Count == 0) return source;
            var ordered = _edits
                .OrderBy(e => e.Start)
                .ThenBy(e => e.Class)
                .ThenBy(e => e.Key)
                .ThenBy(e => e.Sequence)
                .ToList();
            var text = new StringBuilder(source.Length + 64);
            var position = 0;
            foreach (var e in ordered)
            {
                if (e.Start > position) text.Append(source, position, e.Start - position);
                text.Append(e.Text);
                position = Math.Max(position, e.End);
            }
            text.Append(source, position, source.Length - position);
            return text.ToString();
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  AST walking
    // ────────────────────────────────────────────────────────────────────

    internal static class Walk
    {
        /// <summary>Every statement of a block and of the blocks nested in it (not function bodies or lambdas).</summary>
        public static IEnumerable<Statement.Statement> Statements(CompositeStatement block)
        {
            foreach (var s in block.StatementsToExecute)
            {
                yield return s;
                foreach (var child in Children(s))
                    foreach (var nested in Statements(child))
                        yield return nested;
            }
        }

        /// <summary>The blocks directly inside a statement.</summary>
        public static IEnumerable<CompositeStatement> Children(Statement.Statement s)
        {
            switch (s)
            {
                case ConditionStatement c:
                    foreach (var kv in c.Cases) yield return kv.Value;
                    break;
                case HandleExceptionStatement h:
                    yield return h.BeginStatement;
                    if (h.RescueStatement != null) yield return h.RescueStatement;
                    if (h.EnsureStatement != null) yield return h.EnsureStatement;
                    break;
                case AbstractLoopStatement loop:
                    // A loop is itself the block of its body.
                    yield return new LoopBody(loop);
                    break;
            }
        }

        /// <summary>The expressions a statement evaluates itself (not those of its nested blocks).</summary>
        public static IEnumerable<IExpression> OwnExpressions(Statement.Statement s)
        {
            switch (s)
            {
                case ExpressionStatement e: yield return e.Expression; break;
                case PrintStatement p: yield return p.Expression; break;
                case ReturnStatement r: if (r.Expression != null) yield return r.Expression; break;
                case AssertStatement a: yield return a.Expression; break;
                case RaiseExceptionStatement r: if (r.Expression != null) yield return r.Expression; break;
                case YieldStatement y: if (y.Expression != null) yield return y.Expression; break;
                case ConditionStatement c:
                    foreach (var kv in c.Cases) yield return kv.Key;
                    break;
                case ForLoopStatement f:
                    yield return f.LowerBound;
                    yield return f.UpperBound;
                    if (f.Step != null) yield return f.Step;
                    break;
                case IterableLoopStatement it: yield return it.Iterable; break;
                case WhileLoopStatement w: yield return w.Condition; break;
            }
        }

        /// <summary>The loop or rescue variable a statement introduces, or null.</summary>
        public static string? BlockVariable(Statement.Statement s) => s switch
        {
            ForLoopStatement f => f.Variable.Name,
            IterableLoopStatement it => it.Variable.Name,
            HandleExceptionStatement h => h.ErrorVariable,
            _ => null,
        };

        /// <summary>
        /// A loop's body as a block of its own, so that walking a loop's
        /// statements does not walk the loop again.
        /// </summary>
        private sealed class LoopBody : CompositeStatement
        {
            public LoopBody(AbstractLoopStatement loop) : base(null!, loop.RowNumber, loop.BlockName)
            {
                StatementsToExecute.AddRange(loop.StatementsToExecute);
            }
        }
    }

    /// <summary>What one body (top level, function, method, lambda or class body) does.</summary>
    internal sealed class BodyFacts
    {
        /// <summary>Every positioned or unpositioned variable use, in walk order; Write for assignment targets.</summary>
        public readonly List<(VariableExpression Variable, bool Write)> Variables = new List<(VariableExpression, bool)>();
        public readonly HashSet<string> BlockVariables = new HashSet<string>(StringComparer.Ordinal);
        public readonly List<LambdaExpression> Lambdas = new List<LambdaExpression>();
        public readonly List<SubtractionOperator> Subtractions = new List<SubtractionOperator>();
        public readonly List<AssignmentOperator> Assignments = new List<AssignmentOperator>();

        public static BodyFacts Of(CompositeStatement body)
        {
            var facts = new BodyFacts();
            foreach (var s in Walk.Statements(body))
            {
                var blockVariable = Walk.BlockVariable(s);
                if (blockVariable != null) facts.BlockVariables.Add(blockVariable);
                foreach (var e in Walk.OwnExpressions(s))
                    facts.Expression(e);
            }
            return facts;
        }

        public static BodyFacts OfExpression(IExpression e)
        {
            var facts = new BodyFacts();
            facts.Expression(e);
            return facts;
        }

        private void Expression(IExpression? e)
        {
            switch (e)
            {
                case null:
                    return;
                case VariableExpression v:
                    Variables.Add((v, false));
                    return;
                case AssignmentOperator a:
                    Assignments.Add(a);
                    Target(a.Left);
                    Expression(a.Right);
                    return;
                case ClassPropertyOperator p:
                    Expression(p.Left);
                    PropertyName(p.Right);
                    return;
                case ClassCastOperator cast:
                    Expression(cast.Left); // the right operand is a class name
                    return;
                case ClassInstanceOfOperator instanceOf:
                    Expression(instanceOf.Left);
                    return;
                case NestedClassInstanceOperator nested:
                    Expression(nested.Left);
                    if (nested.Right is ClassExpression nestedClass)
                        foreach (var arg in nestedClass.PropertiesExpressions) Expression(arg);
                    return;
                case SubtractionOperator sub:
                    Subtractions.Add(sub);
                    Expression(sub.Left);
                    Expression(sub.Right);
                    return;
                case BinaryOperatorExpression b:
                    Expression(b.Left);
                    Expression(b.Right);
                    return;
                case UnaryOperatorExpression u:
                    Expression(u.Value);
                    return;
                case FunctionExpression f:
                    foreach (var arg in f.ArgumentExpression) Expression(arg);
                    return;
                case ClassExpression c:
                    foreach (var arg in c.PropertiesExpressions) Expression(arg);
                    return;
                case ArrayExpression array:
                    foreach (var value in array.Values) Expression(value);
                    return;
                case LambdaExpression lambda:
                    Lambdas.Add(lambda); // its own body
                    return;
            }
        }

        private void Target(IExpression target)
        {
            switch (target)
            {
                case VariableExpression v:
                    Variables.Add((v, true));
                    break;
                case ArrayValueOperator index:
                    // x{i} = ... reads x (the array is changed, not the variable).
                    Expression(index.Left);
                    Expression(index.Right);
                    break;
                case ClassPropertyOperator p:
                    Expression(p.Left);
                    PropertyName(p.Right);
                    break;
                default:
                    Expression(target);
                    break;
            }
        }

        /// <summary>The right operand of <c>::</c>: a property or method name, not a variable.</summary>
        private void PropertyName(IExpression right)
        {
            switch (right)
            {
                case FunctionExpression method:
                    foreach (var arg in method.ArgumentExpression) Expression(arg);
                    break;
                case ArrayValueOperator index:
                    if (!(index.Left is VariableExpression)) Expression(index.Left);
                    Expression(index.Right);
                    break;
                case VariableExpression _:
                    break;
                default:
                    Expression(right);
                    break;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Module set: symbol tables and rules
    // ────────────────────────────────────────────────────────────────────

    internal sealed class ClassInfo
    {
        public readonly string Name;
        public readonly ClassDefinition Definition;
        public readonly ParsedFile File;
        public readonly HashSet<string> OwnFields = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string>? AllFields;

        public ClassInfo(ClassDefinition definition, ParsedFile file)
        {
            Name = definition.ClassDetails.Name;
            Definition = definition;
            File = file;
            foreach (var p in definition.ClassDetails.Properties) OwnFields.Add(p);
            if (definition.Statement != null)
            {
                foreach (var s in definition.Statement.StatementsToExecute)
                    if (s is ExpressionStatement e && e.Expression is AssignmentOperator a && a.Left is VariableExpression v)
                        OwnFields.Add(v.Name);
            }
        }
    }

    internal enum BodyKind { TopLevel, Function, Method, Lambda, ClassBody }

    internal sealed class Body
    {
        public readonly BodyKind Kind;
        public readonly ParsedFile File;
        public readonly string Name;
        public readonly CompositeStatement? Statements;
        public readonly IReadOnlyList<string> Parameters;
        public readonly ClassInfo? Class;
        /// <summary>The <c>fun</c> token of a function, method or lambda.</summary>
        public readonly int Line, Column;

        public Body(BodyKind kind, ParsedFile file, string name, CompositeStatement? statements,
            IReadOnlyList<string> parameters, ClassInfo? @class, int line, int column)
        {
            Kind = kind;
            File = file;
            Name = name;
            Statements = statements;
            Parameters = parameters;
            Class = @class;
            Line = line;
            Column = column;
        }
    }

    internal sealed class ModuleSet
    {
        private readonly List<ParsedFile> _files;
        private readonly HashSet<string> _globals;
        private readonly Dictionary<string, ClassInfo> _classes = new Dictionary<string, ClassInfo>(StringComparer.Ordinal);
        private readonly HashSet<string> _allFields = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Body> _bodies = new List<Body>();

        public ModuleSet(List<ParsedFile> files, HashSet<string> hostGlobals)
        {
            _files = files;
            _globals = new HashSet<string>(hostGlobals, StringComparer.Ordinal);
        }

        public void Analyse()
        {
            foreach (var file in _files)
            {
                // Top-level assigned names: the direct statements of the file
                // (const and enum included). A name assigned only inside a
                // top-level block was a block variable in v1.
                foreach (var s in file.TopLevel.StatementsToExecute)
                    if (s is ExpressionStatement e && e.Expression is AssignmentOperator a && a.Left is VariableExpression v)
                        _globals.Add(v.Name);

                _bodies.Add(new Body(BodyKind.TopLevel, file, "<top level>", file.TopLevel, Array.Empty<string>(), null, 0, 0));
                CollectDefinitions(file, file.Definitions, null);
            }
            foreach (var c in _classes.Values) _allFields.UnionWith(Fields(c, new HashSet<string>(StringComparer.Ordinal)));

            // Lambdas are found while walking, and may nest.
            for (var i = 0; i < _bodies.Count; i++)
                AnalyseBody(_bodies[i]);

            foreach (var file in _files)
            {
                BlockEscapes(file);
                ImportOrder(file);
                GlobalNames(file);
            }
        }

        private void CollectDefinitions(ParsedFile file, DefinitionScope scope, ClassInfo? owner)
        {
            foreach (var c in scope.ClassDefinitions)
            {
                var info = new ClassInfo(c, file);
                if (!_classes.ContainsKey(info.Name)) _classes[info.Name] = info;
                if (c.Statement != null)
                    _bodies.Add(new Body(BodyKind.ClassBody, file, info.Name, c.Statement, Array.Empty<string>(), info,
                        c.Statement.RowNumber ?? 0, c.Statement.Column));
                CollectDefinitions(file, c.GetDefinitionScope(), info);
            }
            foreach (var f in scope.Functions)
            {
                if (f.Statement == null) continue;
                var kind = owner != null ? BodyKind.Method : BodyKind.Function;
                var name = owner != null ? owner.Name + "#" + f.Details.Name : f.Details.Name;
                _bodies.Add(new Body(kind, file, name, f.Statement, f.Details.Arguments, owner,
                    f.Statement.RowNumber ?? 0, f.Statement.Column));
                // A function nested in a function or method follows the function rules.
                CollectDefinitions(file, f.DefinitionScope, null);
            }
        }

        /// <summary>The fields of a class and its bases, resolved by name in the module set.</summary>
        private HashSet<string> Fields(ClassInfo c, HashSet<string> visiting)
        {
            if (c.AllFields != null) return c.AllFields;
            var fields = new HashSet<string>(c.OwnFields, StringComparer.Ordinal);
            if (visiting.Add(c.Name))
            {
                foreach (var b in c.Definition.BaseTypes)
                    if (_classes.TryGetValue(b.Name, out var baseClass))
                        fields.UnionWith(Fields(baseClass, visiting));
                visiting.Remove(c.Name);
            }
            return c.AllFields = fields;
        }

        private void AnalyseBody(Body body)
        {
            var facts = body.Statements != null ? BodyFacts.Of(body.Statements) : new BodyFacts();
            foreach (var lambda in facts.Lambdas)
                _bodies.Add(new Body(BodyKind.Lambda, body.File, "<lambda>", lambda.Body, lambda.Parameters, null, lambda.Line, lambda.Column));

            var locals = new HashSet<string>(body.Parameters, StringComparer.Ordinal);
            locals.UnionWith(facts.BlockVariables);

            switch (body.Kind)
            {
                case BodyKind.Function:
                case BodyKind.Lambda:
                    GlobalWrites(body, facts, locals);
                    break;
                case BodyKind.Method:
                    MethodNames(body, facts, locals);
                    break;
                case BodyKind.ClassBody:
                    ClassBodyReads(body, facts);
                    break;
            }

            TextSubtractions(body, facts);
            DynamicProperties(body, facts);
            TextIndexes(body, facts);
        }

        // ── A function or lambda writes a global ────────────────────────

        private void GlobalWrites(Body body, BodyFacts facts, HashSet<string> locals)
        {
            var names = facts.Variables
                .Where(x => x.Write && x.Variable.Line > 0 && !locals.Contains(x.Variable.Name) && _globals.Contains(x.Variable.Name))
                .Select(x => x.Variable);
            var firstUse = new Dictionary<string, (int Line, int Column)>(StringComparer.Ordinal);
            foreach (var (v, _) in facts.Variables)
            {
                if (v.Line == 0) continue;
                if (!firstUse.TryGetValue(v.Name, out var p) || (v.Line, v.Column).CompareTo(p) < 0)
                    firstUse[v.Name] = (v.Line, v.Column);
            }
            var ordered = names.Select(v => v.Name).Distinct()
                .OrderBy(n => firstUse[n].Line).ThenBy(n => firstUse[n].Column)
                .ToList();
            if (ordered.Count == 0) return;

            var declaration = "global " + string.Join(", ", ordered);
            var what = body.Kind == BodyKind.Lambda ? "this lambda" : $"`{body.Name}`";
            var entry = body.File.Entry(body.Line, body.Column, Rules.GlobalWrite, EntryKind.Rewrite,
                $"Inserted `{declaration}` as the first statement of {what} (v1 wrote these globals). Decide whether each was intended or should become a local.");
            var group = GlobalDeclaration(body, declaration);
            if (group == null || !body.File.Edits.TryAdd(group))
            {
                entry.Kind = EntryKind.Manual;
                entry.Action = $"Insert `{declaration}` as the first statement of {what} by hand (the tool could not find where its body starts).";
            }
            body.File.Entries.Add(entry);
        }

        /// <summary>The edit that inserts a declaration before the first statement of a function or lambda body.</summary>
        private static EditList.Group? GlobalDeclaration(Body body, string declaration)
        {
            var file = body.File;
            var fun = file.TokenIndexAt(body.Line, body.Column);
            if (fun < 0 || file.Tokens[fun].Value != "fun") return null;

            // The header: `fun name [params]` or `fun [params]`; defaults may hold brackets.
            var i = file.NextToken(fun);
            if (i < 0) return null;
            if (body.Kind != BodyKind.Lambda)
            {
                if (file.Tokens[i].Type != TokenType.Variable) return null;
                var afterName = file.NextToken(i);
                if (afterName >= 0 && IsDivider(file.Tokens[afterName], "[")) i = afterName;
            }
            if (IsDivider(file.Tokens[i], "["))
            {
                var depth = 0;
                for (; i < file.Tokens.Count; i++)
                {
                    if (IsDivider(file.Tokens[i], "[")) depth++;
                    else if (IsDivider(file.Tokens[i], "]") && --depth == 0) break;
                }
                if (i >= file.Tokens.Count) return null;
            }
            var headerEnd = file.Tokens[i];
            var first = file.NextToken(i);
            if (first < 0) return null;
            var firstToken = file.Tokens[first];

            if (firstToken.RowNumber > headerEnd.RowNumber)
            {
                // A line of its own, indented like the first statement.
                var lineStart = file.LineStart(firstToken.RowNumber);
                var at = file.Offset(firstToken.RowNumber, firstToken.Column);
                var indent = file.Source.Substring(lineStart, at - lineStart);
                if (indent.Trim().Length != 0) return null;
                return new EditList.Group().Open(lineStart, indent + declaration + file.NewLine);
            }
            // A one-line body: `fun [x] count = x end`.
            return new EditList.Group().Open(file.TokenOffset(first), declaration + " ");
        }

        private static bool IsDivider(Token.Token t, string value) => t.Type == TokenType.GroupDivider && t.Value == value;

        // ── Methods: fields by bare name, global writes and reads ───────

        private void MethodNames(Body body, BodyFacts facts, HashSet<string> locals)
        {
            var fields = body.Class != null ? Fields(body.Class, new HashSet<string>(StringComparer.Ordinal)) : new HashSet<string>();
            // Names the method assigns that are not fields: v1 method locals.
            var methodLocals = new HashSet<string>(locals, StringComparer.Ordinal);
            foreach (var (v, write) in facts.Variables)
                if (write && !fields.Contains(v.Name)) methodLocals.Add(v.Name);

            var fieldUses = new Dictionary<string, List<VariableExpression>>(StringComparer.Ordinal);
            var reportedWrites = new HashSet<string>(StringComparer.Ordinal);
            var reportedReads = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<VariableExpression>();
            foreach (var (v, write) in facts.Variables)
            {
                if (v.Line == 0 || !seen.Add(v)) continue; // `x += 1` uses one node twice
                var name = v.Name;
                if (locals.Contains(name)) continue;
                if (fields.Contains(name))
                {
                    if (!fieldUses.TryGetValue(name, out var uses)) fieldUses[name] = uses = new List<VariableExpression>();
                    uses.Add(v);
                }
                else if (write && _globals.Contains(name) && reportedWrites.Add(name))
                {
                    body.File.Entries.Add(body.File.Entry(v.Line, v.Column, Rules.MethodWritesGlobal, EntryKind.Manual,
                        $"`{body.Name}` assigns `{name}`, which is a global: v1 made it a method local (globals were invisible in methods), and v2 does too, but the local now shadows the global. No rewrite; check that a local was meant."));
                }
                else if (!write && !methodLocals.Contains(name) && _globals.Contains(name) && reportedReads.Add(name))
                {
                    body.File.Entries.Add(body.File.Entry(v.Line, v.Column, Rules.MethodReadsGlobal, EntryKind.Manual,
                        $"`{body.Name}` reads `{name}`, which is neither a field nor a local but a global: v1 read null here, v2 reads the global's value."));
                }
            }

            foreach (var pair in fieldUses.OrderBy(p => p.Value[0].Line).ThenBy(p => p.Value[0].Column))
            {
                var uses = pair.Value;
                var group = new EditList.Group();
                var ok = true;
                foreach (var v in uses)
                {
                    var at = body.File.Offset(v.Line, v.Column);
                    if (at < 0 || string.CompareOrdinal(body.File.Source, at, v.Name, 0, v.Name.Length) != 0) { ok = false; break; }
                    group.Open(at, "@");
                }
                var count = uses.Count == 1 ? "1 use" : FormattableString.Invariant($"{uses.Count} uses");
                var entry = body.File.Entry(uses[0].Line, uses[0].Column, Rules.FieldAccess, EntryKind.Rewrite,
                    $"Replaced the bare field name `{pair.Key}` with `@{pair.Key}` in `{body.Name}` ({count}).");
                if (!ok || !body.File.Edits.TryAdd(group))
                {
                    entry.Kind = EntryKind.Manual;
                    entry.Action = $"Replace the bare field name `{pair.Key}` with `@{pair.Key}` in `{body.Name}` by hand ({count}).";
                }
                body.File.Entries.Add(entry);
            }
        }

        private void ClassBodyReads(Body body, BodyFacts facts)
        {
            var fields = Fields(body.Class!, new HashSet<string>(StringComparer.Ordinal));
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (v, write) in facts.Variables)
            {
                if (write || v.Line == 0 || fields.Contains(v.Name) || facts.BlockVariables.Contains(v.Name)) continue;
                if (_globals.Contains(v.Name) && reported.Add(v.Name))
                    body.File.Entries.Add(body.File.Entry(v.Line, v.Column, Rules.MethodReadsGlobal, EntryKind.Manual,
                        $"The body of class `{body.Name}` reads `{v.Name}`, which is not a field but a global: v1 read null here, v2 reads the global's value."));
            }
        }

        // ── Text subtraction with a literal ─────────────────────────────

        private static bool IsStaticText(IExpression e) => e switch
        {
            ConstantExpression c => c.Value.IsText,
            // An interpolated string is `"text" + (hole) + ...` (or starts
            // with the hole); a `+` with a text operand is text in v1.
            AdditionOperator a => IsStaticText(a.Left) || IsStaticText(a.Right),
            _ => false,
        };

        private static bool IsNumber(IExpression e) =>
            e is ConstantExpression c && c.Value.IsNumeric
            || e is NegateOperator n && IsNumber(n.Value);

        private static void TextSubtractions(Body body, BodyFacts facts)
        {
            var file = body.File;
            foreach (var sub in facts.Subtractions)
            {
                if (!IsStaticText(sub.Left) && !IsStaticText(sub.Right)) continue;
                if (IsNumber(sub.Left) || IsNumber(sub.Right))
                {
                    // `5 - "5"` is not a text removal: v2 raises TypeError (DEC-8).
                    file.Entries.Add(file.Entry(sub.Line, sub.Column, Rules.TextSubtraction, EntryKind.Manual,
                        "Subtracts text and a number: v2 raises TypeError (DEC-8). Decide what was meant; not rewritten as `Text_remove`."));
                    continue;
                }
                var entry = file.Entry(sub.Line, sub.Column, Rules.TextSubtraction, EntryKind.Rewrite,
                    "Rewrote `a - b` as `Text_remove [a, b]` (text subtraction is removed in v2, PROP-7).");
                var group = TextRemove(file, sub);
                if (group == null || !file.Edits.TryAdd(group))
                {
                    entry.Kind = EntryKind.Manual;
                    entry.Action = "Rewrite this text subtraction as `Text_remove [a, b]` by hand (text subtraction is removed in v2, PROP-7; the tool could not rewrite it here: a compound `-=`, an interpolation hole or a comment between the operands).";
                }
                file.Entries.Add(entry);
            }
        }

        private static EditList.Group? TextRemove(ParsedFile file, SubtractionOperator sub)
        {
            var op = file.TokenIndexAt(sub.Line, sub.Column);
            if (op < 0 || file.Tokens[op].Value != "-") return null; // `-=` and desugared operators
            var left = file.SourceRange(sub.Left);
            var right = file.SourceRange(sub.Right);
            if (left == null || right == null) return null;
            var (ls, le) = left.Value;
            var (rs, re) = right.Value;
            var between = file.Source.Substring(le, rs - le);
            if (le > rs || between.Trim() != "-") return null;
            var length = re - ls;
            return new EditList.Group()
                .Open(ls, "Text_remove [", length)
                .Replace(le, rs, ", ")
                .Close(re, "]", length);
        }

        // ── Dynamic properties and text indexes ─────────────────────────

        private void DynamicProperties(Body body, BodyFacts facts)
        {
            foreach (var a in facts.Assignments)
            {
                if (!(a.Left is ClassPropertyOperator p)) continue;
                var name = p.Right switch
                {
                    VariableExpression v => v.Name,
                    ArrayValueOperator { Left: VariableExpression v } => v.Name,
                    _ => null,
                };
                if (name == null || _allFields.Contains(name)) continue;
                var target = p.Left is ThisExpression ? "this" : "obj";
                body.File.Entries.Add(body.File.Entry(p.Line, p.Column, Rules.DynamicProperty, EntryKind.Manual,
                    $"`{target} :: {name} = ...` creates the property `{name}`, which is not a field of any class in the module set: declare the field (v2 has no dynamic properties)."));
            }
        }

        private static void TextIndexes(Body body, BodyFacts facts)
        {
            // A variable is statically text when every assignment to it in
            // this body is a text literal (or starts with one).
            var textVariables = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var a in facts.Assignments)
            {
                if (!(a.Left is VariableExpression v)) continue;
                var isText = IsStaticText(a.Right);
                textVariables[v.Name] = textVariables.TryGetValue(v.Name, out var before) ? before && isText : isText;
            }
            foreach (var a in facts.Assignments)
            {
                if (!(a.Left is ArrayValueOperator index)) continue;
                var text = IsStaticText(index.Left)
                    || (index.Left is VariableExpression v && textVariables.TryGetValue(v.Name, out var t) && t);
                if (!text) continue;
                body.File.Entries.Add(body.File.Entry(index.Line, index.Column, Rules.TextIndex, EntryKind.Manual,
                    "Assigns an index of a text value: a compile error in v2 (PROP-28), TypeError where it cannot be seen statically. Build the new text instead."));
            }
        }

        // ── Top-level block variables that escape ───────────────────────

        private void BlockEscapes(ParsedFile file)
        {
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            // name → where the block that first assigns it starts
            var escaping = new Dictionary<string, (int Line, int Column)>(StringComparer.Ordinal);
            var reported = new HashSet<string>(StringComparer.Ordinal);

            foreach (var s in file.TopLevel.StatementsToExecute)
            {
                // Reads in this statement (its blocks included) of names that escaped an earlier block.
                if (escaping.Count > 0)
                {
                    // A name this statement binds itself (a loop or rescue
                    // variable, or an assignment in one of its blocks) is
                    // taken to be that binding, not the escaped one: an
                    // approximation that misses a read before such an
                    // assignment, but does not report a new binding.
                    var uses = VariablesIn(s).ToList();
                    var bound = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var (v, write) in uses) if (write) bound.Add(v.Name);
                    var own = Walk.BlockVariable(s);
                    if (own != null) bound.Add(own);
                    foreach (var block in Walk.Children(s))
                        foreach (var inner in Walk.Statements(block))
                            if (Walk.BlockVariable(inner) is string name) bound.Add(name);

                    foreach (var (v, write) in uses)
                    {
                        if (write || v.Line == 0 || bound.Contains(v.Name) || !escaping.TryGetValue(v.Name, out var where) || !reported.Add(v.Name)) continue;
                        file.Entries.Add(file.Entry(v.Line, v.Column, Rules.BlockEscape, EntryKind.Manual,
                            FormattableString.Invariant($"`{v.Name}` is first assigned inside the block at line {where.Line} and read after it: v1 read null here, v2 reads the value.")));
                    }
                }

                if (s is ExpressionStatement e && e.Expression is AssignmentOperator a && a.Left is VariableExpression direct)
                {
                    assigned.Add(direct.Name);
                    escaping.Remove(direct.Name);
                    continue;
                }

                var blocks = Walk.Children(s).ToList();
                if (blocks.Count == 0) continue;
                foreach (var block in blocks)
                    foreach (var inner in Walk.Statements(block))
                        foreach (var expression in Walk.OwnExpressions(inner))
                            foreach (var (v, write) in BodyFacts.OfExpression(expression).Variables)
                                if (write && !assigned.Contains(v.Name) && !escaping.ContainsKey(v.Name))
                                    escaping[v.Name] = (s.RowNumber ?? 0, s.Column);
            }
        }

        private static IEnumerable<(VariableExpression, bool)> VariablesIn(Statement.Statement s)
        {
            foreach (var e in Walk.OwnExpressions(s))
                foreach (var x in BodyFacts.OfExpression(e).Variables) yield return x;
            foreach (var block in Walk.Children(s))
                foreach (var inner in Walk.Statements(block))
                    foreach (var e in Walk.OwnExpressions(inner))
                        foreach (var x in BodyFacts.OfExpression(e).Variables) yield return x;
        }

        // ── Imports after other statements ──────────────────────────────

        private static void ImportOrder(ParsedFile file)
        {
            // Where the file's leading comments and imports end: imports move there.
            var topLevel = file.TopLevel.StatementsToExecute;
            var leadingImports = 0;
            while (leadingImports < topLevel.Count && topLevel[leadingImports] is ImportStatement first
                   && !CodeBefore(file, first)) leadingImports++;

            for (var i = leadingImports; i < topLevel.Count; i++)
            {
                if (!(topLevel[i] is ImportStatement import)) continue;
                var line = import.RowNumber ?? 0;
                var executableBefore = false;
                for (var j = 0; j < i; j++)
                    if (!(topLevel[j] is ImportStatement)) executableBefore = true;

                if (executableBefore)
                {
                    file.Entries.Add(file.Entry(line, import.Column, Rules.ImportOrder, EntryKind.Manual,
                        "This import comes after other statements: moving it to the top would change the order the files initialize in. v2 requires imports first (5.8): move it and check the initialization order by hand."));
                    continue;
                }
                if (!CodeBefore(file, import)) continue; // only comments and imports before it

                // Only definitions (functions, classes) come before it.
                var entry = file.Entry(line, import.Column, Rules.ImportOrder, EntryKind.Rewrite,
                    "Moved this import to the top of the file: only comments, imports and definitions came before it, so initialization order is unchanged.");
                var group = MoveImport(file, import, leadingImports > 0 ? (ImportStatement)topLevel[leadingImports - 1] : null);
                if (group == null || !file.Edits.TryAdd(group))
                {
                    entry.Kind = EntryKind.Manual;
                    entry.Action = "Move this import to the top of the file by hand (only comments, imports and definitions come before it; the tool moves only an import on a line of its own).";
                }
                file.Entries.Add(entry);
            }

            foreach (var s in file.TopLevel.StatementsToExecute)
                foreach (var block in Walk.Children(s))
                    foreach (var inner in Walk.Statements(block))
                        if (inner is ImportStatement nested) ReportNested(file, nested);
            foreach (var body in NestedBodies(file.Definitions))
                foreach (var inner in Walk.Statements(body))
                    if (inner is ImportStatement nested) ReportNested(file, nested);
        }

        private static void ReportNested(ParsedFile file, ImportStatement import) =>
            file.Entries.Add(file.Entry(import.RowNumber ?? 0, import.Column, Rules.ImportOrder, EntryKind.Manual,
                "This import is inside a block or a function: v2 allows imports only at the top of a file (5.8). Move it there by hand and check the initialization order."));

        private static IEnumerable<CompositeStatement> NestedBodies(DefinitionScope scope)
        {
            foreach (var c in scope.ClassDefinitions)
            {
                if (c.Statement != null) yield return c.Statement;
                foreach (var b in NestedBodies(c.GetDefinitionScope())) yield return b;
            }
            foreach (var f in scope.Functions)
            {
                if (f.Statement != null) yield return f.Statement;
                foreach (var b in NestedBodies(f.DefinitionScope)) yield return b;
            }
        }

        /// <summary>
        /// Whether a token other than a comment, or one of a leading import,
        /// comes before <paramref name="import"/>'s keyword.
        /// </summary>
        private static bool CodeBefore(ParsedFile file, ImportStatement import)
        {
            var at = file.TokenIndexAt(import.RowNumber ?? 0, import.Column);
            if (at < 0) return true;
            for (var i = 0; i < at; i++)
            {
                var t = file.Tokens[i];
                if (ParsedFile.IsEmpty(t)) continue;
                if (t.Type == TokenType.Keyword && t.Value == "import")
                {
                    i = file.NextToken(i); // its path
                    continue;
                }
                return true;
            }
            return false;
        }

        private static EditList.Group? MoveImport(ParsedFile file, ImportStatement import, ImportStatement? lastLeading)
        {
            var at = file.TokenIndexAt(import.RowNumber ?? 0, import.Column);
            var path = file.NextToken(at);
            if (at < 0 || path < 0) return null;
            var line = import.RowNumber ?? 0;
            // The import must be alone on its line (a trailing comment may follow).
            for (var i = 0; i < file.Tokens.Count; i++)
            {
                var t = file.Tokens[i];
                if (t.RowNumber != line || i == at || i == path || t.Type == TokenType.LineBreak || t.Type == TokenType.Comment) continue;
                return null;
            }
            var lineStart = file.LineStart(line);
            var lineEnd = file.NextLineStart(line);
            var text = file.Source.Substring(lineStart, lineEnd - lineStart);
            if (lineEnd == file.Source.Length) text += file.NewLine;

            int insertAt;
            if (lastLeading != null)
            {
                insertAt = file.NextLineStart(lastLeading.RowNumber ?? 0);
            }
            else
            {
                // Before the first line that holds a token other than a comment.
                var first = -1;
                for (var i = 0; i < file.Tokens.Count && first < 0; i++)
                    if (!ParsedFile.IsEmpty(file.Tokens[i])) first = i;
                insertAt = first < 0 ? 0 : file.LineStart(file.Tokens[first].RowNumber);
            }
            if (insertAt >= lineStart) return null;
            var group = new EditList.Group().Open(insertAt, text.TrimStart(' ', '\t'), int.MaxValue);
            group.Replace(lineStart, lineEnd, "");
            return group;
        }

        // ── Variables named `global` ────────────────────────────────────

        private static void GlobalNames(ParsedFile file)
        {
            for (var i = 0; i < file.Tokens.Count; i++)
            {
                var t = file.Tokens[i];
                if (t.Type != TokenType.Variable || t.Value != "global") continue;
                var at = file.Offset(t.RowNumber, t.Column);
                var entry = file.Entry(t.RowNumber, t.Column, Rules.GlobalName, EntryKind.Rewrite,
                    "Renamed `global` to `global_`: `global` is a keyword in v2 (PROP-10). Check that `global_` is not already used.");
                if (at < 0 || string.CompareOrdinal(file.Source, at, "global", 0, 6) != 0
                    || !file.Edits.TryAdd(new EditList.Group().Replace(at, at + 6, "global_")))
                {
                    entry.Kind = EntryKind.Manual;
                    entry.Action = "Rename `global` (a keyword in v2, PROP-10) by hand.";
                }
                file.Entries.Add(entry);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Report
    // ────────────────────────────────────────────────────────────────────

    internal static class Report
    {
        /// <summary>The Markdown report (deterministic: no dates, ordinal order).</summary>
        public static string Markdown(MigrationResult result, IEnumerable<string> roots, bool rewritten)
        {
            var nl = "\n";
            var text = new StringBuilder();
            text.Append("# WarScript migration report").Append(nl).Append(nl);
            text.Append("Written by `Tools~/WarScript.Migrate` (plan T3.2, section 8.2) ")
                .Append(rewritten ? "in `--rewrite` mode: the rewrites below were applied." : "in `--report` mode: the rewrites below were not applied.")
                .Append(nl).Append(nl);
            text.Append("Inputs: ").Append(string.Join(", ", roots.Select(r => "`" + r + "`"))).Append(nl).Append(nl);
            var manual = result.Entries.Count(e => e.Kind == EntryKind.Manual);
            text.Append(FormattableString.Invariant(
                $"{result.FileCount} files, {result.Rewritten.Count} with rewrites; {result.Entries.Count} entries: {result.Entries.Count - manual} rewrites, {manual} need manual work."))
                .Append(nl).Append(nl);

            text.Append("| Rule | Meaning | Rewrites | Manual |").Append(nl);
            text.Append("|---|---|---|---|").Append(nl);
            foreach (var (id, description) in Rules.All)
            {
                var of = result.Entries.Where(e => e.Rule == id).ToList();
                text.Append(FormattableString.Invariant(
                    $"| `{id}` | {description} | {of.Count(e => e.Kind == EntryKind.Rewrite)} | {of.Count(e => e.Kind == EntryKind.Manual)} |")).Append(nl);
            }

            foreach (var group in result.Entries.GroupBy(e => e.File))
            {
                text.Append(nl).Append("## ").Append(group.Key).Append(nl).Append(nl);
                foreach (var e in group)
                {
                    text.Append("- `").Append(e.Location).Append("` **").Append(e.Rule).Append("** (")
                        .Append(e.Kind == EntryKind.Rewrite ? "rewrite" : "manual").Append("): ").Append(e.Action).Append(nl);
                    text.Append("  ```").Append(nl).Append("  ").Append(e.SourceLine).Append(nl).Append("  ```").Append(nl);
                }
            }
            return text.ToString();
        }
    }
}
