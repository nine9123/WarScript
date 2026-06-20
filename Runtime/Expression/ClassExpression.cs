#nullable enable

using System.Collections.Generic;
using WarScript.Context;
using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public class ClassExpression : IExpression
    {
        private readonly string _name;
        private readonly List<IExpression> _propertiesExpressions;
        private readonly WarScriptLanguage _script;

        // Compiler accessors
        internal string ClassName => _name;
        internal List<IExpression> PropertiesExpressions => _propertiesExpressions;

        public ClassExpression(WarScriptLanguage script, string name, List<IExpression> propertiesExpressions)
        {
            _script = script;
            _name = name;
            _propertiesExpressions = propertiesExpressions;
        }

        public WarValue Evaluate()
        {
            return EvaluateWith(new Dictionary<string, ClassData>());
        }

        /// <summary>
        /// Evaluate nested class
        /// </summary>
        public WarValue Evaluate(ClassData parentClassData)
        {
            _script.DefinitionContext.PushScope(parentClassData.Definition.GetDefinitionScope());
            try
            {
                return EvaluateWith(new Dictionary<string, ClassData>());
            }
            finally
            {
                _script.DefinitionContext.EndScope();
            }
        }

        private WarValue EvaluateWith(Dictionary<string, ClassData> relations)
        {
            // evaluate property expressions into ValueReferences
            // If the expression is already a ValueReference (from a parent constructor),
            // InstanceOf returns the SAME object — this is how derived and base class
            // scopes share property references for inheritance.
            var values = new List<ValueReference>(_propertiesExpressions.Count);
            foreach (var expression in _propertiesExpressions)
            {
                var valRef = ValueReference.InstanceOf(expression);
                if (_script.HaltFlags != 0) return default;
                values.Add(valRef!);
            }

            // get class's definition
            var definition = _script.DefinitionContext.GetScope().GetClass(_name);
            if (definition == null)
                return _script.RaiseException($"Class '{_name}' is not defined");

            var classStatement = definition.Statement;

            // set separate scope (non-poolable, class instances outlive the scope stack)
            var classScope = new MemoryScope(_script, null, poolable: false);
            _script.MemoryContext.PushScope(classScope);

            // create class data
            var classData = new ClassData(definition, classScope, relations);
            relations[_name] = classData;

            // fill missing properties with Null
            var propCount = definition.ClassDetails.Properties.Count;
            var valuesToSet = new ValueReference[propCount];
            for (var i = 0; i < propCount; i++)
            {
                valuesToSet[i] = i < values.Count
                    ? values[i]
                    : ValueReference.InstanceOf(WarValue.Null);
            }

            // invoke constructors of base classes
            // Pass the SAME ValueReference objects so both scopes share them
            foreach (var baseType in definition.BaseTypes)
            {
                var baseClassProperties = new List<IExpression>();
                foreach (var property in baseType.Properties)
                {
                    var index = definition.ClassDetails.Properties.IndexOf(property);
                    baseClassProperties.Add(valuesToSet[index]);
                }
                var baseExpression = new ClassExpression(_script, baseType.Name, baseClassProperties);
                baseExpression.EvaluateWith(relations);
            }

            try
            {
                _script.ClassInstanceContext.PushValue(classData);
                for (var i = 0; i < propCount; i++)
                {
                    // Use SetLocal(name, ValueReference) overload to store the shared reference
                    _script.MemoryContext.GetScope().SetLocal(definition.ClassDetails.Properties[i], valuesToSet[i]);
                }

                // execute constructor body
                _script.DefinitionContext.PushScope(definition.GetDefinitionScope());
                try
                {
                    classStatement.Execute();
                }
                finally
                {
                    _script.DefinitionContext.EndScope();
                }

                if (_script.ExceptionContext.IsRaised())
                    return default;

                return WarValue.FromClass(classData);
            }
            finally
            {
                _script.MemoryContext.EndScope();
                _script.ClassInstanceContext.PopValue();
            }
        }
    }
}
