using System.Collections.Generic;

namespace WarScript.Context.Definition
{
    /// <summary>
    /// Details for a function
    ///
    /// <see cref="FunctionDefinition"/>
    /// </summary>
    public class FunctionDetails
    {
        /// <summary>
        /// Function's name
        /// </summary>
        public string Name { get; private set; }
        
        /// <summary>
        /// Names of the function arguments
        /// </summary>
        public List<string> Arguments { get; private set; }

        public FunctionDetails(string name, List<string> arguments)
        {
            Name = name;
            Arguments = arguments;
        }
        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null || GetType() != obj.GetType()) return false;
            var other = (FunctionDetails)obj;
            return Name == other.Name && Arguments.Count == other.Arguments.Count;
        }
 
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Name != null ? Name.GetHashCode() : 0;
                hash = hash * 397 + Arguments.Count;
                return hash;
            }
        }
    }
}