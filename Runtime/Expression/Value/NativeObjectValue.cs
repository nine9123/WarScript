namespace WarScript.Expression.Value
{
    public class NativeObjectValue<T> : Value<T>, INativeObjectValue
    {
        public object GetRawValue()
        {
            return GetObjectValue();
        }

        public NativeObjectValue(WarScriptLanguage script, T value) : base(script, value)
        {
        }

        public override string ToString()
        {
            return $"[NativeObject: {typeof(T).Name}]";
        }
    }
}