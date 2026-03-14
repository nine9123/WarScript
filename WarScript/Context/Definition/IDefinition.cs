namespace WarScript.Context.Definition
{
    /// <summary>
    /// Interface to specify structures supported by WarScript
    /// 
    /// <see cref="ClassDefinition"/>
    /// <see cref="FunctionDefinition"/>
    /// </summary>
    public interface IDefinition
    {
        /// <summary>
        /// Contains nested structures declared in this definition
        /// </summary>
        /// <returns></returns>
        public DefinitionScope GetDefinitionScope();
    }
}