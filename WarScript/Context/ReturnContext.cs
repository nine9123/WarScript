namespace WarScript.Context
{
    /// <summary>
    /// Associates a given <see cref="ReturnScope"/> with <see cref="Statement.CompositeStatement"/>
    ///
    /// <see cref="Statement.Loop.AbstractLoopStatement"/>
    /// <see cref="Statement.ReturnStatement"/>
    /// <see cref="Expression.FunctionExpression"/>
    /// </summary>
    public class ReturnContext
    {
        private static ReturnScope _scope = new ReturnScope();

        /// <summary>
        /// Get current <see cref="ReturnScope"/>
        /// </summary>
        /// <returns></returns>
        public static ReturnScope GetScope()
        {
            return _scope;
        }

        /// <summary>
        /// Reset state of the <see cref="ReturnContext"/> on block exit
        /// </summary>
        public static void Reset()
        {
            _scope = new ReturnScope();
        }
    }
}