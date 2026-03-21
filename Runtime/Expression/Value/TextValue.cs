namespace WarScript.Expression.Value
{
    public sealed class TextValue : ComparableValue<string>
    {
        public TextValue(WarScriptLanguage script, string value) : base(script, value)
        {
        }

        public IValue GetValue(int index)
        {
            if (GetValue().Length > index)
                return new TextValue(_script, GetValue().Substring(index, 1));
            
            return _script.Null;
        }

        public void SetValue(int index, IValue value)
        {
            if (GetValue().Length > index)
            {
                SetValue(GetValue().Substring(0, index) + value + GetValue().Substring(index));
            }
        }
    }
}