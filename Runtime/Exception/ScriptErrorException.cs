namespace WarScript.Exception
{
    /// <summary>
    /// The kinds of runtime error a script can raise (v2 plan, section 5.9).
    /// v1 errors still reach scripts as message text only; the kind is carried
    /// by <see cref="ScriptErrorException"/> now so natives can be written
    /// against it, and becomes visible to the host with structured errors
    /// (plan T2.5).
    /// </summary>
    public enum ScriptErrorKind
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
        LimitExceeded
    }

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
