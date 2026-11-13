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
        void Resume();
    }
}
