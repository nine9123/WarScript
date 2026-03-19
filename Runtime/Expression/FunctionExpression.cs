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
        private readonly WarScriptLanguage _script;

        public FunctionExpression(WarScriptLanguage script, string name, List<IExpression> argumentExpression)
        {
            _script = script;
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
                return _script.ExceptionContext.RaiseException($"Function '{classValue.GetValue().ClassDetails.Name}#{Name} [{args}]' is not defined");
            }
            var classDefinitionScope = classDefinition.GetDefinitionScope();
            var functionClassValue = classValue.GetRelation(classDefinition.ClassDetails.Name);
            var memoryScope = functionClassValue.MemoryScope;
            
            // set class's definition and memory scopes
            _script.DefinitionContext.PushScope(classDefinitionScope);
            _script.MemoryContext.PushScope(memoryScope);
            _script.ClassInstanceContext.PushValue(functionClassValue);

            try
            {
                // proceed function
                return Evaluate(values);
            }
            finally
            {
                _script.DefinitionContext.EndScope();
                _script.MemoryContext.EndScope();
                _script.ClassInstanceContext.PopValue();
            }
        }

        private IValue? Evaluate(List<IValue> values)
        {
            // get function's definition and statement
            var definition = _script.DefinitionContext.GetScope().GetFunction(Name, values.Count);
            if (definition == null)
            {
                var args = "";
                for (var i = 0; i < values.Count; i++)
                {
                    args += $"arg {values[i]}";
                    if (i < values.Count - 1)
                        args += ", ";
                }
                return _script.ExceptionContext.RaiseException($"Function '{Name} [{args}]' is not defined");
            }
            
            // Native binding
            if (definition is NativeFunctionDefinition nativeFunctionDefinition)
            {
                try
                {
                    return nativeFunctionDefinition.NativeBody(values);
                }
                catch (System.Exception e)
                {
                    return _script.ExceptionContext.RaiseException(
                        $"Native function '{Name}' failed: {e.Message}"
                    );
                }
            }
            
            // User-defined function
            var statement = definition.Statement;
            var details = definition.Details;
            
            // set new memory scope
            _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());

            try
            {
                // initialize function arguments
                for (var i = 0; i < details.Arguments.Count; i++)
                {
                    _script.MemoryContext.GetScope().SetLocal(details.Arguments[i], values.Count > i ? values[i] : _script.Null);
                }
                
                //execute function body
                statement.Execute();
                
                // obtain function result
                return _script.ReturnContext.GetScope().Result;
            }
            finally
            {
                // release function memory and return context
                _script.MemoryContext.EndScope();
                _script.ReturnContext.Reset();
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