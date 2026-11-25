#nullable enable

using System;
using System.Collections.Generic;

namespace WarScript.Native
{
    /// <summary>
    /// The formatters a host registered for its native object types through
    /// <see cref="WarScriptLanguage.RegisterNativeFormatter{T}"/> (v2 plan
    /// T1.6, PROP-14). One per instance: the runtime never calls a host
    /// object's own <c>ToString()</c>, and a type without a formatter prints
    /// as <c>&lt;TypeName&gt;</c>.
    /// </summary>
    public sealed class NativeFormatters
    {
        private readonly Dictionary<Type, Func<object, string>> _formatters =
            new Dictionary<Type, Func<object, string>>();

        /// <summary>True when no formatter is registered.</summary>
        public bool IsEmpty => _formatters.Count == 0;

        /// <summary>
        /// Registers (or replaces) the formatter for objects whose runtime
        /// type is exactly <typeparamref name="T"/>; derived types need their
        /// own registration.
        /// </summary>
        public void Register<T>(Func<T, string> formatter)
        {
            if (formatter == null) throw new ArgumentNullException(nameof(formatter));
            _formatters[typeof(T)] = o => formatter((T)o);
        }

        /// <summary>
        /// Formats <paramref name="value"/> with the formatter registered for
        /// its type. False when there is none. A formatter that returns null
        /// counts as none.
        /// </summary>
        public bool TryFormat(object value, out string text)
        {
            if (_formatters.TryGetValue(value.GetType(), out var formatter))
            {
                var result = formatter(value);
                if (result != null)
                {
                    text = result;
                    return true;
                }
            }
            text = "";
            return false;
        }
    }
}
