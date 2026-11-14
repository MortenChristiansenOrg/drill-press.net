using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

internal sealed class NullableOriginWalker(int maxDepth, int maxResults)
{
    private readonly List<NullableOriginValue> _values = [];
    private readonly List<ExpressionTraversalBoundary> _boundaries = [];
    private readonly HashSet<SyntaxNode> _visited = [];
    private readonly HashSet<ISymbol> _entries = new(SymbolEqualityComparer.Default);

    internal NullableOriginAnalysis Analyze(CodeExpression root)
    {
        Visit(root, 0);
        var origin =
            _values.Any(value => value.Origin == NullableOrigin.Oblivious)
                ? NullableOrigin.Oblivious
            : _boundaries.Count > 0 || _values.Count == 0 ? NullableOrigin.Unknown
            : NullableOrigin.Aware;
        return new(origin, _values.AsReadOnly(), _boundaries.AsReadOnly());
    }

    private void Visit(CodeExpression expression, int depth)
    {
        if (!expression.IsImplicit && !_visited.Add(expression.Syntax))
            return;
        var results = expression.ResultValues(maxDepth, maxResults);
        _boundaries.AddRange(results.Boundaries);
        foreach (var result in results.Values)
        {
            expression.Source.Project.CancellationToken.ThrowIfCancellationRequested();
            if (_values.Count == maxResults)
            {
                Stop(result.Expression, ExpressionTraversalReason.ExpressionLimit);
                return;
            }
            // The left of ?? supplies only non-null values, whatever its declaration says.
            if (result.Kind != ResultValueKind.Unproven || result.IsNonNullByLanguage)
                Add(result.Expression, NullableOrigin.Aware);
            else
                Classify(result.Expression, depth);
        }
    }

    private void Classify(CodeExpression expression, int depth)
    {
        var operation = expression.Operation.WithoutConversions();
        while (operation is IConditionalAccessOperation access)
            operation = access.WhenNotNull.WithoutConversions();
        switch (operation)
        {
            case ILocalReferenceOperation { Local: var local }
                when expression.Source.ReachingAssignments.IsPatternMatched(local):
            case IInstanceReferenceOperation:
            // String concatenation always creates a string.
            case IBinaryOperation
            {
                OperatorMethod: null,
                Type.SpecialType: SpecialType.System_String
            }:
                Add(expression, NullableOrigin.Aware);
                break;
            case ILocalReferenceOperation { Local: var local }:
                Follow(expression, local, depth);
                break;
            case IParameterReferenceOperation { Parameter: var parameter }:
                Follow(expression, parameter, depth);
                break;
            case IAwaitOperation when expression.Syntax is AwaitExpressionSyntax awaited:
                if (
                    expression.Source.Model.GetAwaitExpressionInfo(awaited).GetResultMethod is
                    { } result
                )
                    Add(expression, result.ReturnNullableAnnotation);
                else
                    Stop(expression, ExpressionTraversalReason.UnsupportedExpression);
                break;
            case ISimpleAssignmentOperation { IsRef: false, Value.Syntax: ExpressionSyntax value }:
                Next(expression, value, depth);
                break;
            case IConversionOperation { IsTryCast: true, Operand.Syntax: ExpressionSyntax operand }:
                Next(expression, operand, depth);
                break;
            case { } declared when Declared(declared) is { } annotation:
                Add(expression, annotation);
                break;
            default:
                Stop(expression, ExpressionTraversalReason.UnsupportedExpression);
                break;
        }
    }

    // The annotation on the declaration that a call or member read takes its value from.
    private static NullableAnnotation? Declared(IOperation operation) =>
        operation switch
        {
            IInvocationOperation call => call.TargetMethod.ReturnNullableAnnotation,
            IPropertyReferenceOperation property => property.Property.NullableAnnotation,
            IFieldReferenceOperation field => field.Field.NullableAnnotation,
            IEventReferenceOperation member => member.Event.NullableAnnotation,
            IArrayElementReferenceOperation { ArrayReference.Type: IArrayTypeSymbol array } =>
                array.ElementNullableAnnotation,
            IImplicitIndexerReferenceOperation { IndexerSymbol: IPropertySymbol indexer } =>
                indexer.NullableAnnotation,
            IImplicitIndexerReferenceOperation { IndexerSymbol: IMethodSymbol slice } =>
                slice.ReturnNullableAnnotation,
            IBinaryOperation { OperatorMethod: { } method } => method.ReturnNullableAnnotation,
            IUnaryOperation { OperatorMethod: { } method } => method.ReturnNullableAnnotation,
            _ => null,
        };

    private void Follow(CodeExpression expression, ISymbol symbol, int depth)
    {
        if (depth == maxDepth)
        {
            Stop(expression, ExpressionTraversalReason.DepthLimit);
            return;
        }
        if (
            expression.Syntax is not IdentifierNameSyntax read
            || expression.Source.ReachingAssignments.At(read, symbol) is not { } reaching
        )
        {
            Stop(expression, ExpressionTraversalReason.UntrackedAssignment);
            return;
        }
        if (reaching.IncludesEntry && _entries.Add(symbol))
            Add(expression, ((IParameterSymbol)symbol).NullableAnnotation);
        foreach (var value in reaching.Assigned)
            Visit(new(expression.Source, value), depth + 1);
    }

    private void Next(CodeExpression expression, ExpressionSyntax value, int depth)
    {
        if (depth == maxDepth)
            Stop(expression, ExpressionTraversalReason.DepthLimit);
        else
            Visit(new(expression.Source, value), depth + 1);
    }

    private void Add(CodeExpression expression, NullableAnnotation annotation) =>
        Add(
            expression,
            annotation == NullableAnnotation.None ? NullableOrigin.Oblivious : NullableOrigin.Aware
        );

    private void Add(CodeExpression expression, NullableOrigin origin) =>
        _values.Add(new(expression, origin));

    private void Stop(CodeExpression expression, ExpressionTraversalReason reason) =>
        _boundaries.Add(new(expression, reason));
}
