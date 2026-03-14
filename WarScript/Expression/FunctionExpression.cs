#nullable enable

using System.Collections.Generic;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public class FunctionExpression : IExpression
    {
        public string Name { get; private set; }
        public List<IExpression> ArgumentExpression { get; private set; }

        public FunctionExpression(string name, List<IExpression> argumentExpression)
        {
            Name = name;
            ArgumentExpression = argumentExpression;
        }

        public IValue? Evaluate()
        {
            // initialize function arguments
            var values = new List<IValue>(ArgumentExpression.Count);
            foreach (var expression in ArgumentExpression)
            {
                var value = expression.Evaluate();
                if (value == null) return null;
                values.Add(value);
            }
            return Evaluate(values);
        }

        /// <summary>
        /// Evaluate class's function
        /// </summary>
        /// <param name="classValue">instance of class where the function is placed in</param>
        public IValue? Evaluate(ClassValue classValue)
        {
            // initialize function arguments
            var values = new List<IValue>(ArgumentExpression.Count);
            foreach (var expression in ArgumentExpression)
            {
                var value = expression.Evaluate();
                if (value == null) return null;
                values.Add(value);
            }
            
            // find a class containing the function
            var classDefinition = FindClassDefinitionContainingFunction(classValue.GetValue(), Name, values.Count);
            if (classDefinition == null)
            {
                var args = "";
                for (var i = 0; i < values.Count; i++)
                {
                    args += $"arg {values[i]}";
                    if (i < values.Count - 1)
                        args += ", ";
                }
                return ExceptionContext.RaiseException($"Function '{classValue.GetValue().ClassDetails.Name}#{Name} [{args}]' is not defined");
            }
            var classDefinitionScope = classDefinition.GetDefinitionScope();
            var functionClassValue = classValue.GetRelation(classDefinition.ClassDetails.Name);
            var memoryScope = functionClassValue.MemoryScope;
            
            // set class's definition and memory scopes
            DefinitionContext.PushScope(classDefinitionScope);
            MemoryContext.PushScope(memoryScope);
            ClassInstanceContext.PushValue(functionClassValue);

            try
            {
                // proceed function
                return Evaluate(values);
            }
            finally
            {
                DefinitionContext.EndScope();
                MemoryContext.EndScope();
                ClassInstanceContext.PopValue();
            }
        }

        private IValue Evaluate(List<IValue> values)
        {
            // get function's definition and statement
            var definition = DefinitionContext.GetScope().GetFunction(Name, values.Count);
            if (definition == null)
            {
                var args = "";
                for (var i = 0; i < values.Count; i++)
                {
                    args += $"arg {values[i]}";
                    if (i < values.Count - 1)
                        args += ", ";
                }
                return ExceptionContext.RaiseException($"Function '{Name} [{args}]' is not defined");
            }
            var statement = definition.Statement;
            var details = definition.Details;
            
            // set new memory scope
            MemoryContext.PushScope(MemoryContext.NewScope());

            try
            {
                // initialize function arguments
                for (var i = 0; i < details.Arguments.Count; i++)
                {
                    MemoryContext.GetScope().SetLocal(details.Arguments[i], values.Count > i ? values[i] : NullValue.Instance);
                }
                
                //execute function body
                statement.Execute();
                
                // obtain function result
                return ReturnContext.GetScope().Result;
            }
            finally
            {
                // release function memory and return context
                MemoryContext.EndScope();
                ReturnContext.Reset();
            }
        }

        /// <summary>
        /// Find a Base class that contains the required function
        /// 
        /// <code>
        /// class A
        ///      fun action
        ///      end
        /// end
        /// 
        /// class B
        /// end
        /// 
        /// b = new B
        /// # Function `action` is not available from the DefinitionScope of class B as it's declared in the class A
        /// b :: action []
        /// </code>
        /// 
        /// </summary>
        private ClassDefinition? FindClassDefinitionContainingFunction(ClassDefinition classDefinition, string functionName, int argumentsSize)
        {
            var definitionScope = classDefinition.GetDefinitionScope();
            if (definitionScope.ContainsFunction(functionName, argumentsSize))
            {
                return classDefinition;
            }
            else
            {
                foreach (var baseType in classDefinition.BaseTypes)
                {
                    var baseTypeDefinition = definitionScope.GetClass(baseType.Name);
                    var functionClassDefinition = FindClassDefinitionContainingFunction(baseTypeDefinition, functionName, argumentsSize);
                    if (functionClassDefinition != null)
                        return functionClassDefinition;
                }
                return null;
            }
        }
    }
}