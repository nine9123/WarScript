namespace WarScript.Expression.Operator
{
    public sealed class AssignmentOperator : BinaryOperatorExpression
    {
        /// <summary>
        /// True for the assignment a <c>const</c> or an <c>enum</c> desugars
        /// to (the declaration of the constant), which the compiler's
        /// constant check (T3.8, <see cref="WarScriptLanguage.CheckConstantWrite"/>)
        /// does not count as a reassignment.
        /// </summary>
        internal bool DeclaresConstant { get; set; }

        public AssignmentOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
