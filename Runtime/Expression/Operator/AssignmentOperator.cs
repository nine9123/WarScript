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

        /// <summary>
        /// True for an assignment the parser's desugar wrote rather than the
        /// script: a default parameter value (<c>b = 1</c> when <c>b</c> is
        /// null) and an enum's members, <c>values</c>, <c>names</c> and
        /// <c>count</c>. Not a reassignment for the constant check (T3.8)
        /// either: it writes a parameter or a field of the enum's class.
        /// </summary>
        internal bool Desugared { get; set; }

        /// <summary>
        /// The operator written for a compound assignment (<c>"+="</c>,
        /// <c>"-="</c>, <c>"*="</c>, <c>"/="</c>), which the parser desugars to
        /// <c>target = target op e</c>; null for a plain <c>=</c>. The compiler
        /// rejects a compound target whose twice-evaluated parts have a side
        /// effect (T3.13, PROP-32).
        /// </summary>
        internal string CompoundOperator { get; set; }

        public AssignmentOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
