#nullable enable

using WarScript.Context.Definition;

namespace WarScript
{
    /// <summary>
    /// A script function of one <see cref="WarScriptLanguage"/> instance, by
    /// name and arity (plan 6.1, T2.6), as <see cref="WarScriptLanguage.GetFunction"/>
    /// returns it. The handle does not hold the compiled function: each call
    /// looks up the function the instance defines now, so a handle stays
    /// valid across <see cref="WarScriptLanguage.Reload"/>,
    /// <see cref="WarScriptLanguage.LoadBytecode"/>, imports and
    /// <c>AddFunction</c>. The lookup is repeated only after the instance's
    /// definitions changed (a version counter), so a call through an
    /// unchanged handle costs one comparison. Calling a handle whose function
    /// no longer exists returns an <c>Error</c> result of kind NameError.
    /// </summary>
    public sealed class FunctionHandle
    {
        /// <summary>The function's name.</summary>
        public string Name { get; }

        /// <summary>The number of arguments the handle calls it with (lookup is by name and arity).</summary>
        public int Arity { get; }

        /// <summary>The instance the function belongs to; a handle can only be called on it.</summary>
        public WarScriptLanguage Instance { get; }

        private FunctionDefinition? _definition;
        private int _version;

        internal FunctionHandle(WarScriptLanguage instance, string name, int arity, FunctionDefinition definition)
        {
            Instance = instance;
            Name = name;
            Arity = arity;
            _definition = definition;
            _version = instance.DefinitionsVersion;
        }

        /// <summary>
        /// The definition the name and arity resolve to now: a script
        /// function, a native one, or null when the instance no longer
        /// defines it.
        /// </summary>
        internal FunctionDefinition? Definition
        {
            get
            {
                var version = Instance.DefinitionsVersion;
                if (_version != version)
                {
                    _definition = Instance.FindFunction(Name, Arity);
                    _version = version;
                }
                return _definition;
            }
        }

        /// <summary>True while the instance defines a function of this name and arity.</summary>
        public bool IsDefined => Definition != null;

        /// <summary><c>name/arity</c>.</summary>
        public override string ToString() => Name + "/" + Arity.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
