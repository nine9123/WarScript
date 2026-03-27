#nullable enable
using System;

namespace WarScript.Attributes
{
    /// <summary>
    /// Marks a class as a WarScript module. The source generator will produce
    /// a Register(script, scope) method that registers all [WsFunction] methods
    /// as native WarScript functions, and a LibraryInfo for the registry.
    ///
    /// The class must be partial. It can be static (pure functions like Math)
    /// or instance-based (engine modules like Unit, Player).
    ///
    /// <code>
    /// [WsModule("math", Description = "Math functions")]
    /// public static partial class MathModule
    /// {
    ///     [WsFunction("pow")]
    ///     public static double Pow(double @base, double exp) => Math.Pow(@base, exp);
    /// }
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class WsModuleAttribute : Attribute
    {
        /// <summary>Module name as it appears in the registry and docs.</summary>
        public string Name { get; }

        /// <summary>Human-readable description for documentation.</summary>
        public string Description { get; set; } = "";

        public WsModuleAttribute(string name) => Name = name;
    }

    /// <summary>
    /// Marks a method as a WarScript-callable function. The source generator
    /// produces the marshaling code (WarValue ↔ C# types) automatically.
    ///
    /// Parameter types are auto-marshaled:
    ///   double       → NumericArg
    ///   int          → (int)NumericArg
    ///   float        → (float)NumericArg
    ///   string       → TextArg
    ///   bool         → LogicalArg (Numeric != 0)
    ///   WarValue     → passthrough (no marshaling)
    ///   List&lt;WarValue&gt; → ArrayArg.ArrayValue
    ///
    /// Return types:
    ///   double/int/float → FromNumeric
    ///   string           → FromText
    ///   bool             → FromLogical
    ///   void             → Null
    ///   WarValue         → passthrough
    ///
    /// Methods can be static or instance. Instance methods capture `this`
    /// in the generated lambda.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class WsFunctionAttribute : Attribute
    {
        /// <summary>
        /// Function name as seen in WarScript. If null/empty, the C# method
        /// name is converted to snake_case (e.g. RemoveAt → remove_at).
        /// </summary>
        public string? Name { get; }

        /// <summary>Documentation string.</summary>
        public string Doc { get; set; } = "";

        /// <summary>Return type description for docs.</summary>
        public string Returns { get; set; } = "";

        public WsFunctionAttribute(string? name = null) => Name = name;
    }

    /// <summary>
    /// Marks a field or property that receives the WarScriptLanguage instance.
    /// The generated Register method assigns it before any functions are called.
    ///
    /// <code>
    /// [WsModule("coroutine")]
    /// public partial class CoroutineModule
    /// {
    ///     [WsScript] private WarScriptLanguage _script;
    /// }
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class WsScriptAttribute : Attribute { }

    /// <summary>
    /// Marks a parameter that should receive the raw args list instead of
    /// being auto-marshaled. Used for variadic functions.
    ///
    /// <code>
    /// [WsFunction("print_all")]
    /// public static void PrintAll([WsRawArgs] List&lt;WarValue&gt; args) { ... }
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class WsRawArgsAttribute : Attribute { }
}
