using System.Collections.Generic;

namespace WarScript.Syntax
{
    public class StructureDefinition
    {
        public string Name { get; private set; }
        public List<string> Arguments { get; private set; }

        public StructureDefinition(string name, List<string> arguments)
        {
            Name = name;
            Arguments = arguments;
        }
    }
}