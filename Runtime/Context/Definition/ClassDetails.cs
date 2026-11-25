using System.Collections.Generic;

namespace WarScript.Context.Definition
{
    /// <summary>
    /// Details of a class
    ///
    /// <see cref="ClassDefinition"/>
    /// <see cref="StatementParser"/>
    /// </summary>
    public class ClassDetails
    {
        /// <summary>
        /// Class's name
        /// </summary>
        public string Name { get; private set; }
        
        /// <summary>
        /// Names of the constructor properties
        /// </summary>
        public List<string> Properties { get; private set; }

        /// <summary>
        /// Property name to index lookup. Built once at construction time.
        /// Used by inline caches for O(1) property access.
        /// </summary>
        internal readonly Dictionary<string, int> PropertyIndex;

        /// <summary>
        /// The fields this class declares, each at its fixed index (5.7,
        /// T3.4): the constructor parameters first, at their
        /// <see cref="PropertyIndex"/>, then the other names the class body
        /// assigns in ordinal order, so a class compiled from source and the
        /// same class loaded from bytecode (which stores the fields in
        /// ordinal order, format 9) index them alike. Only the parameters
        /// until the fields are known (<see cref="ClassDefinition.Fields"/>
        /// is set): a class the host registered, or one that names base
        /// types, never has more. A base class's fields are indexed by the
        /// base's own details, the class part that holds them.
        /// </summary>
        public IReadOnlyList<string> Fields => _fields;

        /// <summary>Field name to its index in <see cref="Fields"/>.</summary>
        internal IReadOnlyDictionary<string, int> FieldIndex => _fieldIndex;

        private List<string> _fields;
        private Dictionary<string, int> _fieldIndex;

        public ClassDetails(string name, List<string> properties)
        {
            Name = name;
            Properties = properties;
            PropertyIndex = new Dictionary<string, int>(properties.Count);
            for (int i = 0; i < properties.Count; i++)
                PropertyIndex[properties[i]] = i;
            SetDeclaredFields(null);
        }

        /// <summary>
        /// Indexes <paramref name="declared"/> (the class's own fields; null
        /// when they are not known) after the constructor parameters, which
        /// are always fields. Called whenever <see cref="ClassDefinition.Fields"/>
        /// is set.
        /// </summary>
        internal void SetDeclaredFields(IEnumerable<string> declared)
        {
            _fields = new List<string>(Properties);
            _fieldIndex = new Dictionary<string, int>(PropertyIndex);
            if (declared == null) return;
            var others = new List<string>();
            foreach (var field in declared)
                if (!_fieldIndex.ContainsKey(field)) others.Add(field);
            others.Sort(System.StringComparer.Ordinal);
            foreach (var field in others)
            {
                if (_fieldIndex.ContainsKey(field)) continue;
                _fieldIndex[field] = _fields.Count;
                _fields.Add(field);
            }
        }
        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null || GetType() != obj.GetType()) return false;
            var other = (ClassDetails)obj;
            return Name == other.Name;
        }
 
        public override int GetHashCode()
        {
            return Name != null ? Name.GetHashCode() : 0;
        }
    }
}
