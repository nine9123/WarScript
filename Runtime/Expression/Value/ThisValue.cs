using WarScript.Context;

namespace WarScript.Expression.Value
{
    public class ThisValue : Value<ClassValue>
    {
        public ThisValue(WarScriptLanguage script) : base(script, null) { }

        public override ClassValue GetValue()
        {
            return _script.ClassInstanceContext.GetValue();
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