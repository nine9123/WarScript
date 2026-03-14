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
        public string Name { get; private set; }
        public List<IExpression?> PropertiesExpressions { get; private set; }
        
        // contains Derived class and all the Base classes chain that Derived class inherits
        public Dictionary<string, ClassValue> Relations { get; private set; }
        
        public ClassExpression(string name, List<IExpression?> propertiesExpressions)
        {
            Name = name;
            PropertiesExpressions = propertiesExpressions;
            Relations = new Dictionary<string, ClassValue>();
        }
        
        public ClassExpression(string name, List<IExpression?> propertiesExpressions, Dictionary<string, ClassValue> relations)
        {
            Name = name;
            PropertiesExpressions = propertiesExpressions;
            Relations = relations;
        }

        public IValue? Evaluate()
        {
            // initialize class's properties
            var values = new List<ValueReference>(PropertiesExpressions.Count);
            foreach (var expression in PropertiesExpressions)
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
            var values = new List<ValueReference>(PropertiesExpressions.Count);
            foreach (var expression in PropertiesExpressions)
            {
                var value = ValueReference.InstanceOf(expression);
                if (value == null) return null;
                values.Add(value);
            }
            
            // set parent class's definition
            var classDefinition = classValue.GetValue();
            DefinitionContext.PushScope(classDefinition.GetDefinitionScope());

            try
            {
                return Evaluate(values);
            }
            finally
            {
                DefinitionContext.EndScope();
            }
        }

        private IValue? Evaluate(List<ValueReference> values)
        {
            // get class's definition and statement
            var definition = DefinitionContext.GetScope().GetClass(Name);
            if (definition == null)
                return ExceptionContext.RaiseException($"Class '{Name}' is not defined");

            var classStatement = definition.Statement;
            
            // set separate scope
            var classScope = new MemoryScope(null);
            MemoryContext.PushScope(classScope);
            
            // initialize constructor arguments
            var classValue = new ClassValue(definition, classScope, Relations);
            Relations.Add(Name, classValue);
            
            // fill the missing properties with NullValue.NULL_INSTANCE
            // class A [arg1, arg2]
            // new A [arg1] -> new A [arg1, null]
            // new A [arg1, arg2, arg3] -> new A [arg1, arg2]
            var valuesToSet = new ValueReference?[definition.ClassDetails.Properties.Count];
            for (var i = 0; i < definition.ClassDetails.Properties.Count; i++)
            {
                valuesToSet[i] = i >= values.Count
                    ? ValueReference.InstanceOf(NullValue.Instance)
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
                var baseExpression = new ClassExpression(baseType.Name, baseClassProperties, Relations);
                baseExpression.Evaluate();
            }
            
            try
            {
                ClassInstanceContext.PushValue(classValue);
                for (var i = 0; i < definition.ClassDetails.Properties.Count; i++)
                {
                    MemoryContext.GetScope().SetLocal(definition.ClassDetails.Properties[i], valuesToSet[i]);
                }
                
                // execute function body
                DefinitionContext.PushScope(definition.GetDefinitionScope());
                try
                {
                    classStatement.Execute();
                }
                finally
                {
                    DefinitionContext.EndScope();
                }
                
                // if exception have been thrown in the constructor
                if (ExceptionContext.IsRaised())
                    return null;

                return classValue;
            }
            finally
            {
                MemoryContext.EndScope();
                ClassInstanceContext.PopValue();
            }
        }
    }
}