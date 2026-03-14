namespace WarScript.Expression.Value
{
    public class TextValue : ComparableValue<string>
    {
        public TextValue(string value) : base(value)
        {
        }

        public IValue GetValue(int index)
        {
            if (GetValue().Length > index)
                return new TextValue(GetValue().Substring(index, 1));
            
            return NullValue.Instance;
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