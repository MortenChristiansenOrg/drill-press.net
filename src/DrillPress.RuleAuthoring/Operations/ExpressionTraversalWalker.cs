using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

internal sealed class ExpressionTraversalWalker(
    ExpressionTraversalStep[] steps,
    int maxDepth,
    int maxExpressions
)
{
    private readonly List<CodeExpression> _values = [];
    private readonly List<ExpressionTraversalBoundary> _boundaries = [];
    private readonly HashSet<(AnalysisSource Source, int Start, int Length)> _seen = [];
    private readonly Stack<(CodeExpression Expression, int Depth)> _pending = [];

    internal ExpressionTraversalResult Traverse(CodeExpression root)
    {
        _pending.Push((root, 0));
        while (_pending.TryPop(out var next))
        {
            root.Source.Project.CancellationToken.ThrowIfCancellationRequested();
            var location = next.Expression.Location;
            if (!_seen.Add((next.Expression.Source, location.Start, location.Length)))
                continue;
            if (_values.Count == maxExpressions)
            {
                Stop(next.Expression, ExpressionTraversalReason.ExpressionLimit);
                break;
            }
            _values.Add(next.Expression);
            Visit(next.Expression, next.Depth);
        }
        var status =
            _boundaries.Any(boundary =>
                boundary.Reason
                    is ExpressionTraversalReason.DepthLimit
                        or ExpressionTraversalReason.ExpressionLimit
            )
                ? ExpressionTraversalStatus.LimitExceeded
            : _boundaries.Count > 0 ? ExpressionTraversalStatus.Unavailable
            : ExpressionTraversalStatus.Complete;
        return new(status, _values.AsReadOnly(), _boundaries.AsReadOnly());
    }

    private void Visit(CodeExpression expression, int depth)
    {
        if (!expression.IsResolved)
        {
            Stop(expression, ExpressionTraversalReason.InvalidBinding);
            return;
        }
        var call = expression.AsInvocation();
        if (call is null)
        {
            if (
                expression.Operation
                is IConversionOperation
                    or IConditionalAccessOperation
                    or IDynamicInvocationOperation
            )
                Stop(expression, ExpressionTraversalReason.UnsupportedExpression);
            return;
        }
        var inputs = Inputs(call).OrderBy(value => value.Location.Start).ToArray();
        if (inputs.Length > 0 && depth == maxDepth)
        {
            Stop(expression, ExpressionTraversalReason.DepthLimit);
            return;
        }
        foreach (var input in inputs.Reverse())
            _pending.Push((input, depth + 1));
    }

    private IEnumerable<CodeExpression> Inputs(CodeInvocation call)
    {
        foreach (var step in steps.Where(step => call.Calls(step.Member)))
        {
            if (step.Parameter is null)
            {
                if (call.Receiver is { IsImplicit: false } receiver)
                    yield return receiver;
                else
                    Stop(call.Expression!, ExpressionTraversalReason.UnavailableInput);
                continue;
            }
            var arguments = call.ArgumentsFor(step.Parameter).ToArray();
            if (arguments.Length == 0 || arguments.Any(argument => argument.Value is null))
                Stop(call.Expression!, ExpressionTraversalReason.UnavailableInput);
            foreach (var argument in arguments)
                if (argument.Value is { } value)
                    yield return value;
        }
    }

    private void Stop(CodeExpression expression, ExpressionTraversalReason reason) =>
        _boundaries.Add(new(expression, reason));
}
