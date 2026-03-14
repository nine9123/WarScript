using WarScript.Context;

namespace WarScript.Expression.Value
{
    public class ThisValue : Value<ClassValue>
    {
        public static readonly ThisValue Instance = new ThisValue();

        public ThisValue() : base(null)
        {
        }

        public override ClassValue GetValue()
        {
            return ClassInstanceContext.GetValue();
        }

        public override IValue Evaluate()
        {
            return GetValue();
        }

        public override string ToString()
        {
            return GetValue().ToString();
        }
    }
}