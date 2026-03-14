namespace WarScript.Context
{
    /// <summary>
    /// Associates a given <see cref="NextScope"/> with a loop block
    ///
    /// <see cref="Statement.Loop.AbstractLoopStatement"/>
    /// <see cref="Statement.Loop.NextStatement"/>
    /// </summary>
    public class NextContext
    {
        private static NextScope _scope = new NextScope();

        /// <summary>
        /// Get current <see cref="NextScope"/>
        /// </summary>
        public static NextScope GetScope()
        {
            return _scope;
        }

        /// <summary>
        /// Reset state of the <see cref="NextScope"/> on loop exit
        /// </summary>
        public static void Reset()
        {
            _scope = new NextScope();
        }
    }
}