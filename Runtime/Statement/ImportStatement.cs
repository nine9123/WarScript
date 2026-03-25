#nullable enable

namespace WarScript.Statement
{
    /// <summary>
    /// Imports function and class definitions from another script file.
    ///
    /// <code>
    /// import "lib/vectors.ws"
    ///
    /// v = new Vector2[3, 4]
    /// print v::magnitude[]
    /// </code>
    ///
    /// The imported file is lexed, parsed, and its top-level code is executed
    /// in an isolated scope. All function and class definitions produced by
    /// that file are then copied into the caller's current definition scope.
    ///
    /// <list type="bullet">
    /// <item>Circular imports are detected and raise an exception.</item>
    /// <item>Each file path is only imported once per script; subsequent
    ///       imports of the same path reuse the cached definitions.</item>
    /// </list>
    ///
    /// <see cref="StatementParser"/>
    /// <see cref="WarScriptLanguage"/>
    /// </summary>
    public class ImportStatement : Statement
    {
        private readonly string _path;

        // Compiler accessor
        internal string Path => _path;
        
        public ImportStatement(WarScriptLanguage script, int rowNumber, string blockName, string path) 
            : base(script, rowNumber, blockName)
        {
            _path = path;
        }

        public override void Execute()
        {
            // Ensure a file resolver has been provided
            if (_script.FileResolver == null)
            {
                _script.ExceptionContext.RaiseException(
                    $"Cannot import '{_path}': no file resolver configured");
                _script.ExceptionContext.AddTracedStatement(this);
                return;
            }

            // Resolve to an absolute/canonical path for caching and cycle detection
            var resolvedPath = _path;

            // Circular import detection
            if (_script.ImportStack.Contains(resolvedPath))
            {
                _script.ExceptionContext.RaiseException(
                    $"Circular import detected: '{resolvedPath}'");
                _script.ExceptionContext.AddTracedStatement(this);
                return;
            }

            // If already imported, reuse the cached definitions
            if (_script.ImportCache.TryGetValue(resolvedPath, out var cachedScope))
            {
                cachedScope.CopyLocalDefinitionsTo(_script.DefinitionContext.GetScope());
                return;
            }

            // Read the source code via the host-provided resolver
            string? sourceCode;
            try
            {
                sourceCode = _script.FileResolver.Invoke(resolvedPath);
            }
            catch (System.Exception e)
            {
                _script.ExceptionContext.RaiseException(
                    $"Failed to read import '{resolvedPath}': {e.Message}");
                _script.ExceptionContext.AddTracedStatement(this);
                return;
            }

            if (sourceCode == null)
            {
                _script.ExceptionContext.RaiseException(
                    $"Import '{resolvedPath}' not found");
                _script.ExceptionContext.AddTracedStatement(this);
                return;
            }

            // Mark as in-progress for cycle detection
            _script.ImportStack.Add(resolvedPath);

            // Capture the caller's scope before pushing the import scope
            var callerScope = _script.DefinitionContext.GetScope();

            // Create isolated scopes for the imported file
            var importDefinitionScope = _script.DefinitionContext.NewScope();

            _script.DefinitionContext.PushScope(importDefinitionScope);

            try
            {
                // Lex
                var tokens = LexicalParser.Parse(sourceCode);

                // Parse (registers function/class definitions into importDefinitionScope)
                var importStatement = new CompositeStatement(_script, null, resolvedPath);
                StatementParser.Parse(_script, tokens, importStatement);

                // Execute top-level code (e.g. nested imports, variable init)
                importStatement.Execute();

                if (_script.ExceptionContext.IsRaised())
                    return;

                // Cache the imported definitions
                _script.ImportCache[resolvedPath] = importDefinitionScope;
            }
            finally
            {
                _script.DefinitionContext.EndScope();
                _script.ImportStack.Remove(resolvedPath);
            }

            // Copy definitions into the caller's scope (after import scope is popped)
            importDefinitionScope.CopyLocalDefinitionsTo(callerScope);
        }
    }
}
