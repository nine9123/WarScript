#nullable enable

using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Bytecode
{
    /// <summary>
    /// Debugger step modes. Set <see cref="DebugContext.Action"/> in the debug
    /// hook callback to control what happens after the breakpoint.
    /// </summary>
    public enum StepMode
    {
        /// <summary>Continue running until the next breakpoint.</summary>
        Continue,

        /// <summary>Stop at the next line in any function (follows calls).</summary>
        StepInto,

        /// <summary>Stop at the next line in the same function (steps over calls).</summary>
        StepOver,

        /// <summary>Run until the current function returns, then stop.</summary>
        StepOut,
    }

    /// <summary>
    /// Callback invoked when the VM hits a breakpoint or step point.
    /// Set <see cref="DebugContext.Action"/> before returning to control execution.
    /// </summary>
    public delegate void DebugHook(DebugContext context);

    /// <summary>
    /// Snapshot of VM state passed to the <see cref="DebugHook"/> callback.
    /// Provides the current source position, call stack, and variable access.
    /// </summary>
    public class DebugContext
    {
        /// <summary>Script name (file name).</summary>
        public string ScriptName { get; }

        /// <summary>Source line number where execution paused.</summary>
        public int Line { get; }

        /// <summary>Name of the function currently executing.</summary>
        public string FunctionName { get; }

        /// <summary>
        /// Full call stack, outermost first. Each entry is (functionName, line).
        /// </summary>
        public IReadOnlyList<StackEntry> CallStack { get; }

        /// <summary>
        /// Local variables visible in the current frame.
        /// Key = variable name, Value = current value.
        /// Hidden compiler locals (prefixed with $) are excluded.
        /// </summary>
        public IReadOnlyDictionary<string, WarValue> Locals { get; }

        /// <summary>
        /// Set this before returning from the callback to control
        /// what the VM does next. Defaults to <see cref="StepMode.Continue"/>.
        /// </summary>
        public StepMode Action { get; set; }

        private readonly WarScriptLanguage _script;

        internal DebugContext(
            WarScriptLanguage script,
            string scriptName,
            int line,
            string functionName,
            IReadOnlyList<StackEntry> callStack,
            IReadOnlyDictionary<string, WarValue> locals)
        {
            _script = script;
            ScriptName = scriptName;
            Line = line;
            FunctionName = functionName;
            CallStack = callStack;
            Locals = locals;
            Action = StepMode.Continue;
        }

        /// <summary>
        /// Read a global variable from the script's memory scope.
        /// </summary>
        public WarValue GetGlobal(string name)
        {
            return _script.UserMemoryScope.Get(name);
        }

        /// <summary>One entry in the call stack.</summary>
        public readonly struct StackEntry
        {
            public readonly string FunctionName;
            public readonly int Line;

            public StackEntry(string functionName, int line)
            {
                FunctionName = functionName;
                Line = line;
            }

            public override string ToString() => $"{FunctionName}:{Line}";
        }
    }
}
