namespace WarScript
{
    /// <summary>
    /// Common interface for tree-walk and bytecode coroutines.
    /// </summary>
    public interface ICoroutine
    {
        int Id { get; }
        bool IsComplete { get; }
        bool IsReady(double dt);
        void Resume();
    }
}
