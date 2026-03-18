using System;
using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Context.Definition
{
    public class NativeFunctionDefinition : FunctionDefinition
    {
        public Func<List<IValue>, IValue> NativeBody { get; private set; }
        public string Doc { get; private set; }
        public string Returns { get; private set; }

        public NativeFunctionDefinition(
            FunctionDetails details,
            Func<List<IValue>, IValue> nativeBody,
            string doc,
            string returns)
            : base(details, null, null)  // no statement, no definition scope
        {
            NativeBody = nativeBody;
            Doc = doc;
            Returns = returns;
        }
    }
}