using WarScript.Expression.Value;

namespace WarScript.Context
{
    /// <summary>
    /// Scope for the <see cref="Statement.CompositeStatement"/> defining if the <b>return</b> statement invoked
    ///
    /// <see cref="BreakContext"/>
    /// </summary>
    public class ReturnScope
    {
        public bool Invoked { get; private set; }
        public IValue Result { get; private set; }

        /// <summary>
        /// Notify current scope that <b>return</b> statement invoked
        /// </summary>
        public void Invoke(IValue result)
        {
            Invoked = true;
            Result = result;
        }
    }
}