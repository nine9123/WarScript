#nullable enable

using System.Collections.Generic;
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

        public WarValue Evaluate()
        {
            var values = new List<WarValue>(ArgumentExpression.Count);
            for (int i = 0; i < ArgumentExpression.Count; i++)
            {
                var value = ArgumentExpression[i].Evaluate();
                if (_script.HaltFlags != 0) return default;
                values.Add(value);
            }
            return Evaluate(values, isClassMethod: false);
        }

        /// <summary>
        /// Evaluate class's function
        /// </summary>
        public WarValue Evaluate(ClassData classData)
        {
            var values = new List<WarValue>(ArgumentExpression.Count);
            for (int i = 0; i < ArgumentExpression.Count; i++)
            {
                var value = ArgumentExpression[i].Evaluate();
                if (_script.HaltFlags != 0) return default;
                values.Add(value);
            }

            var classDefinition = FindClassDefinitionContainingFunction(classData.Definition, Name, values.Count);
            if (classDefinition == null)
            {
                var args = "";
                for (var i = 0; i < values.Count; i++)
                {
                    args += $"arg {values[i]}";
                    if (i < values.Count - 1) args += ", ";
                }
                return _script.RaiseException($"Function '{classData.Definition.ClassDetails.Name}#{Name} [{args}]' is not defined");
            }

            var classDefinitionScope = classDefinition.GetDefinitionScope();
            var functionClassData = classData.GetRelation(classDefinition.ClassDetails.Name);
            var memoryScope = functionClassData!.MemoryScope;

            _script.DefinitionContext.PushScope(classDefinitionScope);
            _script.MemoryContext.PushScope(memoryScope);
            _script.ClassInstanceContext.PushValue(functionClassData);

            try
            {
                return Evaluate(values, isClassMethod: true);
            }
            finally
            {
                _script.DefinitionContext.EndScope();
                _script.MemoryContext.EndScope();
                _script.ClassInstanceContext.PopValue();
            }
        }

        private WarValue Evaluate(List<WarValue> values, bool isClassMethod)
        {
            var definition = _script.DefinitionContext.GetScope().GetFunction(Name, values.Count);
            if (definition == null)
            {
                var args = "";
                for (var i = 0; i < values.Count; i++)
                {
                    args += $"arg {values[i]}";
                    if (i < values.Count - 1) args += ", ";
                }
                return _script.RaiseException($"Function '{Name} [{args}]' is not defined");
            }

            // Native binding
            if (definition is NativeFunctionDefinition nativeFn)
            {
                try
                {
                    return nativeFn.NativeBody(values);
                }
                catch (System.Exception e)
                {
                    return _script.RaiseException($"Native function '{Name}' failed: {e.Message}");
                }
            }

            // User-defined function
            var statement = definition.Statement;
            var details = definition.Details;

            if (isClassMethod)
                _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());
            else
                _script.MemoryContext.PushScope(
                    _script.MemoryContext.NewScope(_script.UserMemoryScope));

            try
            {
                for (var i = 0; i < details.Arguments.Count; i++)
                {
                    _script.MemoryContext.GetScope().SetLocal(
                        details.Arguments[i],
                        values.Count > i ? values[i] : WarValue.Null);
                }

                statement.Execute();

                return _script.ReturnContext.GetScope().Result;
            }
            finally
            {
                _script.MemoryContext.EndScope();
                _script.ReturnContext.Reset();
                _script.HaltFlags &= ~WarScriptLanguage.HaltFlag.Return;
            }
        }

        private ClassDefinition? FindClassDefinitionContainingFunction(ClassDefinition classDefinition, string functionName, int argumentsSize)
        {
            var definitionScope = classDefinition.GetDefinitionScope();
            if (definitionScope.ContainsFunction(functionName, argumentsSize))
                return classDefinition;

            foreach (var baseType in classDefinition.BaseTypes)
            {
                var baseTypeDefinition = definitionScope.GetClass(baseType.Name);
                if (baseTypeDefinition == null) continue;
                var result = FindClassDefinitionContainingFunction(baseTypeDefinition, functionName, argumentsSize);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
}
