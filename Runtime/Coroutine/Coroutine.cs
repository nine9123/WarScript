#nullable enable

using System.Collections.Generic;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Expression;
using WarScript.Expression.Value;
using WarScript.Statement;

namespace WarScript
{
    public class Coroutine : ICoroutine
    {
        public int Id { get; }
        public bool IsComplete { get; private set; }

        private readonly WarScriptLanguage _script;
        private readonly FunctionDefinition _function;
        private readonly DefinitionScope _definitionScope;
        private readonly MemoryScope _memoryScope;
        private readonly bool _loop;

        private readonly List<CoroutineSegment> _segments;
        private int _currentSegment;
        private Dictionary<string, WarValue> _savedVariables;

        private YieldType _yieldType;
        private double _waitRemaining;
        private IExpression? _untilCondition;

        public Coroutine(
            WarScriptLanguage script,
            FunctionDefinition function,
            DefinitionScope definitionScope,
            MemoryScope memoryScope,
            WarValue[] args,
            bool loop,
            int id)
        {
            _script = script;
            _function = function;
            _definitionScope = definitionScope;
            _memoryScope = memoryScope;
            _loop = loop;
            Id = id;
            _currentSegment = 0;

            _savedVariables = new Dictionary<string, WarValue>();
            for (var i = 0; i < function.Details.Arguments.Count; i++)
            {
                _savedVariables[function.Details.Arguments[i]] =
                    i < args.Length ? args[i] : WarValue.Null;
            }

            _segments = SplitSegments(function.Statement);
        }

        public bool IsReady(double dt)
        {
            switch (_yieldType)
            {
                case YieldType.NextTick: return true;
                case YieldType.Wait:
                    _waitRemaining -= dt;
                    return _waitRemaining <= 0;
                case YieldType.Until: return EvaluateUntilCondition();
                default: return true;
            }
        }

        public void Resume()
        {
            if (_currentSegment >= _segments.Count)
            {
                IsComplete = true;
                return;
            }

            var segment = _segments[_currentSegment];

            _script.DefinitionContext.PushScope(_definitionScope);
            _script.MemoryContext.PushScope(_memoryScope);
            _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());
            _script.ClearYield();

            try
            {
                foreach (var kvp in _savedVariables)
                    _script.MemoryContext.GetScope().SetLocal(kvp.Key, kvp.Value);

                foreach (var stmt in segment.Statements)
                {
                    stmt.Execute();
                    if (_script.ExceptionContext.IsRaised()) break;
                    if (_script.ReturnContext.GetScope().Invoked) break;
                }

                if (segment.Yield != null && !_script.ExceptionContext.IsRaised()
                                          && !_script.ReturnContext.GetScope().Invoked)
                {
                    segment.Yield.Execute();
                }

                _savedVariables = _script.MemoryContext.GetScope().GetAllLocals();
            }
            finally
            {
                _script.MemoryContext.EndScope();
                _script.MemoryContext.EndScope();
                _script.DefinitionContext.EndScope();
                _script.ReturnContext.Reset();
                _script.HaltFlags &= ~WarScriptLanguage.HaltFlag.Return;

                if (_script.ExceptionContext.IsRaised())
                    _script.ExceptionContext.PrintStackTrace();
            }

            _currentSegment++;

            if (segment.Yield != null && _script.IsYielded)
            {
                _yieldType = _script.YieldedType;
                _waitRemaining = _script.YieldedWaitDuration;
                _untilCondition = _script.YieldedType == YieldType.Until
                    ? segment.Yield.Expression
                    : null;
                _script.ClearYield();
            }
            else if (_currentSegment >= _segments.Count)
            {
                if (_loop) { _currentSegment = 0; _yieldType = YieldType.NextTick; }
                else { IsComplete = true; }
            }
            else
            {
                _yieldType = YieldType.NextTick;
            }
        }

        private bool EvaluateUntilCondition()
        {
            if (_untilCondition == null) return true;

            _script.DefinitionContext.PushScope(_definitionScope);
            _script.MemoryContext.PushScope(_memoryScope);
            _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());

            try
            {
                foreach (var kvp in _savedVariables)
                    _script.MemoryContext.GetScope().SetLocal(kvp.Key, kvp.Value);

                var result = _untilCondition.Evaluate();
                return result.IsLogical && result.LogicalValue;
            }
            finally
            {
                _script.MemoryContext.EndScope();
                _script.MemoryContext.EndScope();
                _script.DefinitionContext.EndScope();
            }
        }

        private List<CoroutineSegment> SplitSegments(FunctionStatement function)
        {
            var segments = new List<CoroutineSegment>();
            var currentStatements = new List<Statement.Statement>();

            foreach (var stmt in function.StatementsToExecute)
            {
                if (stmt is YieldStatement yield)
                {
                    segments.Add(new CoroutineSegment(currentStatements, yield, 0));
                    currentStatements = new List<Statement.Statement>();
                }
                else
                {
                    currentStatements.Add(stmt);
                }
            }

            segments.Add(new CoroutineSegment(currentStatements, null, 0));
            return segments;
        }
    }

    public class CoroutineSegment
    {
        public readonly List<Statement.Statement> Statements;
        public readonly YieldStatement? Yield;
        public double YieldWaitDuration;

        public CoroutineSegment(
            List<Statement.Statement> statements,
            YieldStatement? yield,
            double yieldWaitDuration)
        {
            Statements = statements;
            Yield = yield;
            YieldWaitDuration = yieldWaitDuration;
        }
    }
}
