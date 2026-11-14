namespace WarScript
{
    /// <summary>
    /// The kinds of runtime error a script can raise (v2 plan, section 5.9).
    /// Scripts still see an error as message text only; the host sees the kind
    /// in <see cref="ScriptError.Kind"/> (plan T2.3). Until structured errors
    /// (T2.5) assign one at every raise, only compile errors, host faults,
    /// native failures and errors thrown as <see cref="ScriptErrorException"/>
    /// have their own kind; every other runtime error reports Raised.
    /// </summary>
    public enum ScriptErrorKind : byte
    {
        Raised,
        TypeError,
        IndexError,
        NameError,
        ArgumentError,
        DivisionByZero,
        AssertionFailed,
        NativeError,
        InstructionBudgetExceeded,
        MemoryBudgetExceeded,
        LimitExceeded,
        /// <summary>The script, or a file it imports, does not compile (plan T2.3; never raised inside a script).</summary>
        CompileError,
        /// <summary>An unexpected C# exception inside the VM (plan 6.3; never raised inside a script).</summary>
        HostFault
    }
}

namespace WarScript.Exception
{
    /// <summary>
    /// Thrown by a native function (or by runtime code running on behalf of
    /// the VM) to raise a script error of a given kind (plan 6.9). The VM
    /// turns it into an ordinary script error with this message, which
    /// <c>begin/rescue</c> can catch. Any other exception thrown by a native
    /// becomes a NativeError; any other exception thrown inside the VM is a
    /// host fault.
    /// </summary>
    public class ScriptErrorException : System.Exception
    {
        public ScriptErrorKind Kind { get; }

        public ScriptErrorException(ScriptErrorKind kind, string message) : base(message)
        {
            Kind = kind;
        }
    }

    /// <summary>
    /// A runtime limit was exceeded (value depth, text or array length and
    /// the like; plan 5.12). Raised as a LimitExceeded script error.
    /// </summary>
    internal sealed class WarScriptLimitException : ScriptErrorException
    {
        public WarScriptLimitException(string message) : base(ScriptErrorKind.LimitExceeded, message)
        {
        }
    }
}
