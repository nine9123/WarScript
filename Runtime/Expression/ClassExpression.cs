#nullable enable

using System.Collections.Generic;
using WarScript.Context;
using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public class ClassExpression : IExpression
    {
        private readonly string _name;
        private readonly List<IExpression?> _propertiesExpressions;
        private readonly WarScriptLanguage _script;
        
        public ClassExpression(WarScriptLanguage script, string name, List<IExpression?> propertiesExpressions)
        {
            _script = script;
            _name = name;
            _propertiesExpressions = propertiesExpressions;
        }

        public IValue? Evaluate()
        {
            // Fresh relations dict per instantiation
            return EvaluateWith(new Dictionary<string, ClassValue>());
        }

        /// <summary>
        /// Evaluate nested class
        /// </summary>
        /// <param name="classValue">instance of the parent class</param>
        public IValue? Evaluate(ClassValue classValue)
        {
            var classDefinition = classValue.GetValue();
            _script.DefinitionContext.PushScope(classDefinition.GetDefinitionScope());

            try
            {
                return EvaluateWith(new Dictionary<string, ClassValue>());
            }
            finally
            {
                _script.DefinitionContext.EndScope();
            }
        }

        /// <summary>
        /// Shared entry point that takes a relations dict.
        /// Base class construction calls this to share the parent's dict.
        /// </summary>
        private IValue? EvaluateWith(Dictionary<string, ClassValue> relations)
        {
            // initialize class's properties
            var values = new List<ValueReference>(_propertiesExpressions.Count);
            foreach (var expression in _propertiesExpressions)
            {
                var value = ValueReference.InstanceOf(expression);
                if (value == null) return null;
                values.Add(value);
            }

            // get class's definition and statement
            var definition = _script.DefinitionContext.GetScope().GetClass(_name);
            if (definition == null)
                return _script.ExceptionContext.RaiseException($"Class '{_name}' is not defined");

            var classStatement = definition.Statement;
            
            // set separate scope
            var classScope = new MemoryScope(_script, null);
            _script.MemoryContext.PushScope(classScope);
            
            // initialize constructor arguments
            var classValue = new ClassValue(_script, definition, classScope, relations);
            relations.Add(_name, classValue);
            
            // fill the missing properties with NullValue.NULL_INSTANCE
            var valuesToSet = new ValueReference?[definition.ClassDetails.Properties.Count];
            for (var i = 0; i < definition.ClassDetails.Properties.Count; i++)
            {
                valuesToSet[i] = i >= values.Count
                    ? ValueReference.InstanceOf(_script.Null)
                    : values[i];
            }
            
            // invoke constructors of the base classes and set a ClassValue relation
            foreach (var baseType in definition.BaseTypes)
            {
                var baseClassProperties = new List<IExpression?>();
                foreach (var property in baseType.Properties)
                {
                    var index = definition.ClassDetails.Properties.IndexOf(property);
                    baseClassProperties.Add(valuesToSet[index]);
                }
                // Base class shares the SAME relations dict: calls EvaluateWith directly,
                // never goes through public Evaluate() which would create a fresh dict.
                var baseExpression = new ClassExpression(_script, baseType.Name, baseClassProperties);
                baseExpression.EvaluateWith(relations);
            }
            
            try
            {
                _script.ClassInstanceContext.PushValue(classValue);
                for (var i = 0; i < definition.ClassDetails.Properties.Count; i++)
                {
                    _script.MemoryContext.GetScope().SetLocal(definition.ClassDetails.Properties[i], valuesToSet[i]);
                }
                
                // execute function body
                _script.DefinitionContext.PushScope(definition.GetDefinitionScope());
                try
                {
                    classStatement.Execute();
                }
                finally
                {
                    _script.DefinitionContext.EndScope();
                }
                
                // if exception have been thrown in the constructor
                if (_script.ExceptionContext.IsRaised())
                    return null;

                return classValue;
            }
            finally
            {
                _script.MemoryContext.EndScope();
                _script.ClassInstanceContext.PopValue();
            }
        }
    }
}