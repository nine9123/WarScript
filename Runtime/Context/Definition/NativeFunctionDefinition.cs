using System;
using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Context.Definition
{
    public class NativeFunctionDefinition : FunctionDefinition
    {
        public Func<List<WarValue>, WarValue> NativeBody { get; private set; }
        public string Doc { get; private set; }
        public string Returns { get; private set; }

        public NativeFunctionDefinition(
            FunctionDetails details,
            Func<List<WarValue>, WarValue> nativeBody,
            string doc,
            string returns)
            : base(details, null, null)
        {
            NativeBody = nativeBody;
            Doc = doc;
            Returns = returns;
        }
    }
}
