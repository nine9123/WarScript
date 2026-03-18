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

        public ClassDetails(string name, List<string> properties)
        {
            Name = name;
            Properties = properties;
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