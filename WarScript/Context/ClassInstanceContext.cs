using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Context
{
    /// <summary>
    /// Associates a given <see cref="Expression.Value.ClassValue"/> with <b>this</b> reference for the current block of code
    ///
    /// <see cref="Expression.Value.ThisValue"/>
    /// <see cref="Expression.ExpressionReader"/>
    /// <see cref="Expression.FunctionExpression"/>
    /// </summary>
    public class ClassInstanceContext
    {
        private static readonly Stack<ClassValue> Values = new Stack<ClassValue>();

        /// <summary>
        /// Get current <b>this</b> reference
        /// </summary>
        /// <returns></returns>
        public static ClassValue GetValue()
        {
            return Values.Peek();
        }

        /// <summary>
        /// Push new <b>this</b> reference when entering a class's constructor or invoking a class's function
        /// </summary>
        public static void PushValue(ClassValue instance)
        {
            Values.Push(instance);
        }

        /// <summary>
        /// Pop <b>this</b> reference on block exit
        /// </summary>
        public static void PopValue()
        {
            Values.Pop();
        }
    }
}