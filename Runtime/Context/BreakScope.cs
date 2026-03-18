namespace WarScript.Context
{
    /// <summary>
    /// Scope for the loop block defining if the <b>break</b> statement invoked
    ///
    /// <see cref="BreakContext"/>
    /// <see cref="Statement.Loop.BreakStatement"/>
    /// </summary>
    public class BreakScope
    {
        public bool Invoked { get; private set; }

        /// <summary>
        /// Notify the loop block about invoking the <b>break</b> statement
        /// </summary>
        public void Invoke()
        {
            Invoked = true;
        }
    }
}