namespace WarScript.Context
{
    /// <summary>
    /// Associates a given <see cref="BreakScope"/> with a loop block
    ///
    /// <see cref="Statement.Loop.AbstractLoopStatement"/>
    /// <see cref="Statement.Loop.BreakStatement"/>
    /// </summary>
    public class BreakContext
    {
        private static BreakScope _scope = new BreakScope();

        /// <summary>
        /// Get current <see cref="BreakScope"/>
        /// </summary>
        public static BreakScope GetScope()
        {
            return _scope;
        }

        /// <summary>
        /// Reset state of the <see cref="BreakContext"/> on loop exit
        /// </summary>
        public static void Reset()
        {
            _scope = new BreakScope();
        }
    }
}