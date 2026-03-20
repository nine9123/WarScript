#nullable enable

using System.Collections.Generic;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Expression;
using WarScript.Expression.Value;
using WarScript.Statement;

namespace WarScript
{
    public class Coroutine
    {
        public readonly int Id;
        public bool IsComplete { get; private set; }

        private readonly WarScriptLanguage _script;
        private readonly FunctionDefinition _function;
        private readonly DefinitionScope _definitionScope;
        private readonly MemoryScope _memoryScope;
        private readonly bool _loop;

        private readonly List<CoroutineSegment> _segments;
        private int _currentSegment;
        private Dictionary<string, IValue> _savedVariables;

        // Yield state
        private YieldType _yieldType;
        private double _waitRemaining;
        private IExpression? _untilCondition;

        public Coroutine(
            WarScriptLanguage script,
            FunctionDefinition function,
            DefinitionScope definitionScope,
            MemoryScope memoryScope,
            IValue[] args,
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

            // Save initial arguments as variables
            _savedVariables = new Dictionary<string, IValue>();
            for (var i = 0; i < function.Details.Arguments.Count; i++)
            {
                _savedVariables[function.Details.Arguments[i]] =
                    i < args.Length ? args[i] : script.Null;
            }

            // Split function body into segments at yield boundaries
            _segments = SplitSegments(function.Statement);
        }

        /// <summary>
        /// Checks if the coroutine is ready to resume.
        /// For Wait: decrements timer. For Until: evaluates condition.
        /// </summary>
        public bool IsReady(double dt)
        {
            switch (_yieldType)
            {
                case YieldType.NextTick:
                    return true;

                case YieldType.Wait:
                    _waitRemaining -= dt;
                    return _waitRemaining <= 0;

                case YieldType.Until:
                    return EvaluateUntilCondition();

                default:
                    return true;
            }
        }

        /// <summary>
        /// Executes the current segment with saved variables.
        /// </summary>
        public void Resume()
        {
            if (_currentSegment >= _segments.Count)
            {
                IsComplete = true;
                return;
            }

            var segment = _segments[_currentSegment];

            // Push scopes — same as Call() does
            _script.DefinitionContext.PushScope(_definitionScope);
            _script.MemoryContext.PushScope(_memoryScope);
            _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());

            // Clear yield state before executing
            _script.ClearYield();

            try
            {
                // Restore saved variables into the function scope
                foreach (var kvp in _savedVariables)
                {
                    _script.MemoryContext.GetScope().SetLocal(kvp.Key, kvp.Value);
                }

                // Execute segment statements
                foreach (var stmt in segment.Statements)
                {
                    stmt.Execute();

                    if (_script.ExceptionContext.IsRaised()) break;
                    if (_script.ReturnContext.GetScope().Invoked) break;
                }

                // Execute the yield statement itself (evaluates wait duration)
                if (segment.Yield != null && !_script.ExceptionContext.IsRaised()
                                          && !_script.ReturnContext.GetScope().Invoked)
                {
                    segment.Yield.Execute();
                }

                // Save all local variables for next segment
                _savedVariables = _script.MemoryContext.GetScope().GetAllLocals();
            }
            finally
            {
                _script.MemoryContext.EndScope();
                _script.MemoryContext.EndScope();
                _script.DefinitionContext.EndScope();
                _script.ReturnContext.Reset();

                if (_script.ExceptionContext.IsRaised())
                    _script.ExceptionContext.PrintStackTrace();
            }

            // Advance to next segment
            _currentSegment++;

            // Set up the yield condition from what YieldStatement.Execute() stored
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
                if (_loop)
                {
                    _currentSegment = 0;
                    _yieldType = YieldType.NextTick;
                }
                else
                {
                    IsComplete = true;
                }
            }
            else
            {
                // No yield but more segments (shouldn't happen, but handle gracefully)
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
                {
                    _script.MemoryContext.GetScope().SetLocal(kvp.Key, kvp.Value);
                }

                var result = _untilCondition.Evaluate();
                return result is LogicalValue lv && lv.GetValue();
            }
            finally
            {
                _script.MemoryContext.EndScope();
                _script.MemoryContext.EndScope();
                _script.DefinitionContext.EndScope();
            }
        }

        /// <summary>
        /// Splits a function body into segments at top-level yield statements.
        /// </summary>
        private List<CoroutineSegment> SplitSegments(FunctionStatement function)
        {
            var segments = new List<CoroutineSegment>();
            var currentStatements = new List<Statement.Statement>();

            foreach (var stmt in function.StatementsToExecute)
            {
                if (stmt is YieldStatement yield)
                {
                    // Evaluate wait duration at split time is wrong —
                    // it needs to be evaluated at execution time.
                    // Store 0 for now, Resume() reads it from the yield's Execute().
                    segments.Add(new CoroutineSegment(currentStatements, yield, 0));
                    currentStatements = new List<Statement.Statement>();
                }
                else
                {
                    currentStatements.Add(stmt);
                }
            }

            // Final segment (after last yield, or entire body if no yields)
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