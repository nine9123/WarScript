using WarScript.Expression.Value;

namespace WarScript.Context
{
    public class ReturnScope
    {
        public bool Invoked { get; private set; }
        public WarValue Result { get; private set; }

        public void Invoke(WarValue result)
        {
            Invoked = true;
            Result = result;
        }

        public void Reset()
        {
            Invoked = false;
            Result = default;
        }
    }
}
