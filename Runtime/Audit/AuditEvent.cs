#nullable enable

namespace WarScript.Audit
{
    /// <summary>
    /// A v1 behavior that changes in WarScript v2 (plan section 8.1, T0.8).
    /// Temporary: the audit hooks are removed once the migration is done
    /// (plan 8.3, step 8).
    /// </summary>
    public enum AuditKind
    {
        /// <summary><c>==</c>/<c>!=</c> on operands of different types, neither null. v1 compares their text; v2 returns false (DEC-8).</summary>
        MixedTypeEquality,
        /// <summary>Ordering (<c>&lt;</c> and friends) that falls back to comparing text. v2: TypeError.</summary>
        MixedTypeOrdering,
        /// <summary>Ordering two logical values. v2: TypeError (PROP-17).</summary>
        LogicalOrdering,
        /// <summary><c>-</c> on non-numbers (the text-replace path). v2: TypeError; use <c>Text_remove</c> (PROP-7).</summary>
        TextSubtraction,
        /// <summary><c>+</c> taking the text path although neither operand is text, e.g. <c>true + 1</c>. v2: TypeError.</summary>
        NonTextConcatenation,
        /// <summary>A logical value formatted by <c>print</c>, concatenation or interpolation. v2 prints <c>true</c>/<c>false</c> (DEC-11).</summary>
        LogicalFormatted,
        /// <summary>Unary minus on a value that is not a number. v2: TypeError.</summary>
        NegateNonNumeric,
        /// <summary><c>+</c> with exactly one array operand. v2: TypeError (PROP-6).</summary>
        ArrayPlusValue,
        /// <summary><c>&lt;&lt;</c> on a target that is not an array. v2: TypeError.</summary>
        AppendToNonArray,
        /// <summary>An index read that yields null: out of bounds, non-numeric index, or a target that cannot be indexed. v2: IndexError or TypeError.</summary>
        IndexReadInvalid,
        /// <summary>An index write that is silently ignored. v2: IndexError or TypeError.</summary>
        IndexWriteInvalid,
        /// <summary>An index write into text (v1 writes a new text back). v2: TypeError, text is immutable.</summary>
        TextIndexAssign,
        /// <summary><c>x :: name = ...</c> where <c>x</c> is not an instance (v1 ignores it). v2: TypeError.</summary>
        PropertyWriteOnNonInstance,
        /// <summary>A property write outside the class body that creates a field the class never declared. v2: NameError (PROP-15).</summary>
        DynamicPropertyCreated,
        /// <summary>A variable read that resolves to nothing (v1 reads null). v2: NameError (DEC-5).</summary>
        UndefinedRead,
        /// <summary>A function or lambda assigning an existing global. v2 needs <c>global</c>. Not raised since T3.4, which made such a name a local.</summary>
        GlobalWriteFromFunction,
        /// <summary>A method reading or writing a field by its bare name. v2 needs <c>@name</c>. Not raised since T3.4: a bare name in a method is a local or a global.</summary>
        FieldAccessViaBareName,
        /// <summary>A method or class body reading null for a name that is a global. v2 reads the global (PROP-20); since T3.4 a method does, so only a class body raises it.</summary>
        GlobalShadowedInClass,
        /// <summary>A top-level read of a name last assigned inside a top-level block, which v1 reads as null. v2 reads the value (DEC-4). Not raised since T3.4 (no block has a scope; T3.5 removed the opcodes).</summary>
        BlockVariableEscapes,
        /// <summary><c>==</c>/<c>!=</c> finding two different instances structurally equal. v2: false (DEC-9).</summary>
        ClassStructuralEquality,
    }

    /// <summary>One occurrence of an <see cref="AuditKind"/>, as passed to <see cref="WarScriptLanguage.AuditSink"/>.</summary>
    public readonly struct AuditEvent
    {
        public readonly AuditKind Kind;

        /// <summary>
        /// The script the code came from: the instance's script name, or the
        /// path of the import that defined it. Code loaded from bytecode does
        /// not record its source and is reported under the instance's name.
        /// </summary>
        public readonly string Script;

        /// <summary>Source line, or 0 when the bytecode has none for the instruction.</summary>
        public readonly int Line;

        /// <summary>The WarScript function running: <c>&lt;script&gt;</c>, a function or method name, <c>&lt;lambda&gt;</c>, or <c>Class#ctor</c>.</summary>
        public readonly string Function;

        /// <summary>The values involved, e.g. <c>number 1 == text "1" (v1: true)</c>.</summary>
        public readonly string Detail;

        public AuditEvent(AuditKind kind, string script, int line, string function, string detail)
        {
            Kind = kind;
            Script = script;
            Line = line;
            Function = function;
            Detail = detail;
        }

        public override string ToString() => System.FormattableString.Invariant($"{Kind} at {Script}:{Line} in {Function}: {Detail}");
    }
}
