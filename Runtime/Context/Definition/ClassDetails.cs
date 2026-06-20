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

        public ClassDetails(string name, List<string> properties)
        {
            Name = name;
            Properties = properties;
            PropertyIndex = new Dictionary<string, int>(properties.Count);
            for (int i = 0; i < properties.Count; i++)
                PropertyIndex[properties[i]] = i;
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
