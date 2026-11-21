#nullable enable

using System.Collections.Generic;

namespace WarScript.Context.Definition
{
    /// <summary>
    /// Contains structures (classes, functions) defined in a block of code
    ///
    /// <see cref="ClassDefinition"/>
    /// <see cref="FunctionDefinition"/>
    /// <see cref="DefinitionContext"/>
    /// </summary>
    public class DefinitionScope
    {
        /// <summary>
        /// Classes defined in the block: lookup by name
        /// </summary>
        private readonly Dictionary<string, ClassDefinition> _classes;

        /// <summary>
        /// All class definitions in this scope (for compiler iteration).
        /// </summary>
        public IEnumerable<ClassDefinition> ClassDefinitions => _classes.Values;

        /// <summary>
        /// Functions declared in the block
        /// </summary>
        public readonly List<FunctionDefinition> Functions;

        /// <summary>
        /// Function lookup
        /// </summary>
        private readonly Dictionary<(string name, int argCount), FunctionDefinition> _functionIndex;

        /// <summary>
        /// Parent DefinitionScope to access the structures defined in outer blocks of code
        /// </summary>
        private DefinitionScope? _parent;

        /// <summary>The scope this one falls back to, or null.</summary>
        internal DefinitionScope? Parent => _parent;

        /// <summary>
        /// Gives this scope another parent: an import resolved for a module
        /// that is not installed yet (T3.7, <c>LoadBytecode</c>) is resolved
        /// under the loaded module's scope and moved under the instance's
        /// once the load succeeds.
        /// </summary>
        internal void Reparent(DefinitionScope? parent) => _parent = parent;

        private readonly WarScriptLanguage _script;

        /// <summary>
        /// What the parser has defined in this scope, for the duplicate
        /// check (PROP-12): created on the first definition, or shared by
        /// every top-level scope of a module set while <c>Compile</c> parses
        /// it (one namespace, 5.8). Null after <see cref="Clear"/>.
        /// </summary>
        internal DeclaredDefinitions? Declared;
        
        public DefinitionScope(WarScriptLanguage script, DefinitionScope? parent)
        {
            _script = script;
            _classes = new Dictionary<string, ClassDefinition>();
            Functions = new List<FunctionDefinition>();
            _functionIndex = new Dictionary<(string, int), FunctionDefinition>();
            _parent = parent;
        }

        /// <summary>
        /// Get ClassDefinition from the current block and from outer blocks of code
        /// </summary>
        /// <param name="name">name of the class</param>
        public ClassDefinition? GetClass(string name)
        {
            if (_classes.TryGetValue(name, out var classDefinition))
                return classDefinition;

            return _parent?.GetClass(name);
        }

        /// <summary>The class of that name defined in this scope itself (not its parents), or null.</summary>
        internal ClassDefinition? GetOwnClass(string name)
            => _classes.TryGetValue(name, out var classDefinition) ? classDefinition : null;

        /// <summary>
        /// Add ClassDefinition to the current block
        /// </summary>
        public void AddClass(ClassDefinition classDefinition)
        {
            _classes[classDefinition.ClassDetails.Name] = classDefinition;
        }

        /// <summary>
        /// Get FunctionDefinition from the current block and from outer blocks of code
        /// </summary>
        /// <param name="name">name of the function</param>
        /// <param name="argumentsSize">count of function arguments, useful in case there are multiple functions with the same name but with different length of arguments declared</param>
        /// <returns></returns>
        public FunctionDefinition? GetFunction(string name, int argumentsSize)
        {
            if (_functionIndex.TryGetValue((name, argumentsSize), out var functionDefinition))
                return functionDefinition;

            return _parent?.GetFunction(name, argumentsSize);
        }

        /// <summary>
        /// Check that DefinitionScope contains the function
        /// </summary>
        /// <param name="name">name of the function</param>
        /// <param name="argumentsSize">amount of function arguments in case there are multiple functions with the same name declared</param>
        /// <returns></returns>
        public bool ContainsFunction(string name, int argumentsSize)
        {
            return GetFunction(name, argumentsSize) != null;
        }

        /// <summary>
        /// Add FunctionDefinition to the current block
        /// </summary>
        public void AddFunction(FunctionDefinition functionDefinition) => AddFunction(functionDefinition, true);

        /// <param name="reportShadowing">
        /// False for a definition copied in from an import
        /// (<see cref="CopyImportedDefinitionsTo"/>): its own file already
        /// reported shadowing a native when it was defined there.
        /// </param>
        private void AddFunction(FunctionDefinition functionDefinition, bool reportShadowing)
        {
            // Check if a native/parent function with the same signature already exists
            if (reportShadowing)
            {
                var existing = _parent?.GetFunction(
                    functionDefinition.Details.Name,
                    functionDefinition.Details.Arguments.Count);

                if (existing is NativeFunctionDefinition)
                    _script.Log($"Shadowing native function '{functionDefinition.Details.Name}'");
            }

            Functions.Add(functionDefinition);
            _script.DefinitionsChanged();
            _functionIndex[(functionDefinition.Details.Name, functionDefinition.Details.Arguments.Count)] = functionDefinition;

            // Register at reduced arities for default parameters.
            // A function `fun f [a, b = 1, c = 2]` with MinArity=1
            // is also reachable as f[a] and f[a, b].
            var details = functionDefinition.Details;
            for (int arity = details.MinArity; arity < details.Arguments.Count; arity++)
            {
                var key = (details.Name, arity);
                if (!_functionIndex.ContainsKey(key))
                    _functionIndex[key] = functionDefinition;
            }
        }

        /// <summary>
        /// Copy all locally defined functions and classes into the target scope.
        /// Used by import to merge an imported file's definitions into the caller's scope.
        /// </summary>
        public void CopyLocalDefinitionsTo(DefinitionScope target)
        {
            foreach (var classDefinition in _classes.Values)
                target.AddClass(classDefinition);

            foreach (var functionDefinition in Functions)
                target.AddFunction(functionDefinition);
        }

        /// <summary>
        /// The definitions copied in from imported files (T3.7, 5.8: the
        /// module set has one namespace): reachable here like this scope's
        /// own, but not part of the file it holds, so
        /// <see cref="Bytecode.BytecodeSerializer"/> leaves them out and
        /// <see cref="CopyImportedDefinitionsTo"/> does not copy them on.
        /// Null until the first import.
        /// </summary>
        private HashSet<object>? _imported;

        /// <summary>Whether <paramref name="definition"/> was copied in from an imported file.</summary>
        internal bool IsImported(object definition) => _imported != null && _imported.Contains(definition);

        /// <summary>
        /// Copies this scope's own functions and classes (not those it
        /// imported itself) into <paramref name="target"/>, marked there as
        /// imported; one already imported there is not added again. How an
        /// import's definitions join the module set's namespace (T3.7).
        /// </summary>
        internal void CopyImportedDefinitionsTo(DefinitionScope target)
        {
            target._imported ??= new HashSet<object>(StateHash.ReferenceComparer.Instance);
            foreach (var classDefinition in _classes.Values)
                if (!IsImported(classDefinition) && target._imported.Add(classDefinition))
                    target.AddClass(classDefinition);

            foreach (var functionDefinition in Functions)
                if (!IsImported(functionDefinition) && target._imported.Add(functionDefinition))
                    target.AddFunction(functionDefinition, false);
        }

        /// <summary>
        /// Remove all locally defined functions and classes.
        /// Used by hot reload to clear old definitions before re-parsing.
        /// Native functions (in the parent scope) are unaffected.
        /// </summary>
        public void Clear()
        {
            _classes.Clear();
            Functions.Clear();
            _functionIndex.Clear();
            _imported = null;
            Declared = null;
            _script.DefinitionsChanged();
        }
    }

    /// <summary>
    /// The functions and classes the parser has defined in a namespace
    /// (a definition scope, or the top level of a module set), with where,
    /// so that a second definition of the same function name and arity, or
    /// of the same class name, is a compile error naming the first (PROP-12).
    /// </summary>
    internal sealed class DeclaredDefinitions
    {
        private readonly Dictionary<string, List<(int MinArity, int MaxArity, string? File, int Line)>> _functions = new();
        private readonly Dictionary<string, (string? File, int Line)> _classes = new();

        /// <summary>
        /// Records a function of <paramref name="name"/> reachable with
        /// <paramref name="minArity"/> to <paramref name="maxArity"/>
        /// arguments, or returns the message for the first definition it
        /// overlaps (and records nothing). <paramref name="file"/> is the file
        /// being parsed, left out of the message when the first is there too.
        /// </summary>
        public string? AddFunction(string name, int minArity, int maxArity, string? file, int line)
        {
            if (!_functions.TryGetValue(name, out var defined))
                _functions[name] = defined = new List<(int, int, string?, int)>();
            foreach (var d in defined)
            {
                if (d.MinArity > maxArity || minArity > d.MaxArity) continue;
                var arity = System.Math.Max(d.MinArity, minArity);
                return System.FormattableString.Invariant(
                    $"Function '{name}' with {arity} argument{(arity == 1 ? "" : "s")} is already defined{Where(d.File, d.Line, file)}");
            }
            defined.Add((minArity, maxArity, file, line));
            return null;
        }

        /// <summary>Records a class, or returns the message for the first definition of its name.</summary>
        public string? AddClass(string name, string? file, int line)
        {
            if (_classes.TryGetValue(name, out var d))
                return $"Class '{name}' is already defined{Where(d.File, d.Line, file)}";
            _classes[name] = (file, line);
            return null;
        }

        private static string Where(string? definedIn, int line, string? parsing)
        {
            var at = line > 0 ? " at line " + line.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            return definedIn == parsing ? at : $" in '{definedIn}'{at}";
        }
    }
}
