using FixMath;

namespace WarScript
{
    /// <summary>
    /// Common interface of coroutines (implemented by <see cref="BytecodeCoroutine"/>).
    /// </summary>
    public interface ICoroutine
    {
        int Id { get; }
        bool IsComplete { get; }
        bool IsReady(F64 dt);
        /// <summary>Runs it to its next yield or its end (a host entry); the error that ended the run, if any.</summary>
        ScriptResult Resume();
    }
}
