namespace WarScript.Expression.Value
{
    public class NumericValue : ComparableValue<double>
    {
        public NumericValue(WarScriptLanguage script, double value) : base(script, value)
        {
        }

        public override string ToString()
        {
            if (GetValue() % 1 == 0)
                return ((int)GetValue()).ToString();
            
            return base.ToString();
        }
    }
}