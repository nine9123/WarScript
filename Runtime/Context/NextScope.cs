namespace WarScript.Context
{
    /// <summary>
    /// Scope for the loop block defining if the <b>next</b> statement invoked
    ///
    /// <see cref="Context.NextContext"/>
    /// </summary>
    public class NextScope
    {
        public bool Invoked { get; private set; }

        /// <summary>
        /// Notify the loop block about invoking the <b>next</b> statement
        /// </summary>
        public void Invoke()
        {
            Invoked = true;
        }

        /// <summary>
        /// Reset the scope for reuse
        /// </summary>
        public void Reset()
        {
            Invoked = false;
        }
    }
}