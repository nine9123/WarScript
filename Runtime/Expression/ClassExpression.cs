#nullable enable

using System.Collections;
using System.Collections.Generic;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public class ClassExpression : IExpression
    {
        private readonly string _name;
        private readonly List<IExpression?> _propertiesExpressions;
        
        // contains Derived class and all the Base classes chain that Derived class inherits
        private readonly Dictionary<string, ClassValue> _relations;

        private readonly WarScriptLanguage _script;
        
        public ClassExpression(WarScriptLanguage script, string name, List<IExpression?> propertiesExpressions)
        {
            _script = script;
            _name = name;
            _propertiesExpressions = propertiesExpressions;
            _relations = new Dictionary<string, ClassValue>();
        }

        private ClassExpression(WarScriptLanguage script, string name, List<IExpression?> propertiesExpressions, Dictionary<string, ClassValue> relations)
        {
            _script = script;
            _name = name;
            _propertiesExpressions = propertiesExpressions;
            _relations = relations;
        }

        public IValue? Evaluate()
        {
            // initialize class's properties
            var values = new List<ValueReference>(_propertiesExpressions.Count);
            foreach (var expression in _propertiesExpressions)
            {
                var value = ValueReference.InstanceOf(expression);
                if (value == null) return null;
                values.Add(value);
            }
            return Evaluate(values);
        }

        /// <summary>
        /// Evaluate nested class
        /// </summary>
        /// <param name="classValue">instance of the parent class</param>
        public IValue? Evaluate(ClassValue classValue)
        {
            // initialize class's properties
            var values = new List<ValueReference>(_propertiesExpressions.Count);
            foreach (var expression in _propertiesExpressions)
            {
                var value = ValueReference.InstanceOf(expression);
                if (value == null) return null;
                values.Add(value);
            }
            
            // set parent class's definition
            var classDefinition = classValue.GetValue();
            _script.DefinitionContext.PushScope(classDefinition.GetDefinitionScope());

            try
            {
                return Evaluate(values);
            }
            finally
            {
                _script.DefinitionContext.EndScope();
            }
        }

        private IValue? Evaluate(List<ValueReference> values)
        {
            // get class's definition and statement
            var definition = _script.DefinitionContext.GetScope().GetClass(_name);
            if (definition == null)
                return _script.ExceptionContext.RaiseException($"Class '{_name}' is not defined");

            var classStatement = definition.Statement;
            
            // set separate scope
            var classScope = new MemoryScope(_script, null);
            _script.MemoryContext.PushScope(classScope);
            
            // initialize constructor arguments
            var classValue = new ClassValue(_script, definition, classScope, _relations);
            _relations.Add(_name, classValue);
            
            // fill the missing properties with NullValue.NULL_INSTANCE
            // class A [arg1, arg2]
            // new A [arg1] -> new A [arg1, null]
            // new A [arg1, arg2, arg3] -> new A [arg1, arg2]
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
                // initialize base class's properties
                // class A [a_arg]
                // class B [b_arg1, b_arg2]: A [b_arg1]
                var baseClassProperties = new List<IExpression?>();
                foreach (var property in baseType.Properties)
                {
                    var index = definition.ClassDetails.Properties.IndexOf(property);
                    baseClassProperties.Add(valuesToSet[index]);
                }
                var baseExpression = new ClassExpression(_script, baseType.Name, baseClassProperties, _relations);
                baseExpression.Evaluate();
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