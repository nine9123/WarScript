using System;
using System.Collections.Generic;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Native;

/// <summary>
/// Central registry of all built-in WarScript standard libraries.
/// Used by the docs exporter and by the engine's setup code.
///
/// To add a new library:
///   1. Create a static class with Register(script, scope)
///   2. Add an entry here
/// </summary>
public static class WarScriptLibraryRegistry
{
    public struct LibraryInfo
    {
        public string Name;
        public string Description;
        public Action<WarScriptLanguage, DefinitionScope> Register;
    }

    /// <summary>
    /// All built-in libraries. Order here = order in docs.
    /// </summary>
    public static readonly LibraryInfo[] Libraries = new[]
    {
        new LibraryInfo
        {
            Name = "Math",
            Description = "Mathematical functions: exponentiation, rounding, clamping, interpolation.",
            Register = MathLibrary.Register
        },
        new LibraryInfo
        {
            Name = "Array",
            Description = "Array manipulation: search, insert, remove, copy.",
            Register = ArrayLibrary.Register
        },
        new LibraryInfo
        {
            Name = "Coroutine",
            Description = "Coroutine lifecycle management: start, stop, loop.",
            Register = CoroutineLibrary.Register
        },
        new LibraryInfo
        {
            Name = "Utility",
            Description = "General-purpose helper functions.",
            Register = UtilityLibrary.Register
        },
    };

    /// <summary>
    /// Registers all built-in libraries into a scope.
    /// Call this from your engine setup alongside game-specific bindings.
    /// </summary>
    public static void RegisterAll(WarScriptLanguage script, DefinitionScope scope)
    {
        foreach (var lib in Libraries)
            lib.Register(script, scope);
    }

    /// <summary>
    /// Collects functions per library by registering each into an isolated scope.
    /// Used by the docs exporter.
    /// </summary>
    public static Dictionary<string, (string Description, List<NativeFunctionDefinition> Functions)>
        CollectLibraryDefinitions(WarScriptLanguage script)
    {
        var result = new Dictionary<string, (string, List<NativeFunctionDefinition>)>();

        foreach (var lib in Libraries)
        {
            var scope = new DefinitionScope(script, null);
            lib.Register(script, scope);
            var functions = new List<NativeFunctionDefinition>();
            foreach (var fn in scope.Functions)
            {
                if (fn is NativeFunctionDefinition nfd)
                    functions.Add(nfd);
            }
            result[lib.Name] = (lib.Description, functions);
        }

        return result;
    }
}