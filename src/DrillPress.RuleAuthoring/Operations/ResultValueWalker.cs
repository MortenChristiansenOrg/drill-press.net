using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

internal sealed class ResultValueWalker(int maxDepth, int maxResults)
{
    private readonly List<ResultValue> _values = [];
    private readonly List<ExpressionTraversalBoundary> _boundaries = [];
    private readonly Stack<Alternative> _pending = [];

    internal ResultValueAnalysis Analyze(CodeExpression root)
    {
        if (root.IsImplicit)
            Stop(root, ExpressionTraversalReason.UnsupportedExpression);
        else
            _pending.Push(new(root.Syntax, root.Syntax, ResultValueRole.Direct, false, false, 0));
        while (_pending.TryPop(out var next))
        {
            root.Source.Project.CancellationToken.ThrowIfCancellationRequested();
            var expression = new CodeExpression(root.Source, next.Syntax);
            if (_values.Count == maxResults)
            {
                Stop(expression, ExpressionTraversalReason.ExpressionLimit);
                break;
            }
            Visit(expression, next);
        }
        return new(
            ExpressionTraversalWalker.StatusOf(_boundaries),
            _values.AsReadOnly(),
            _boundaries.AsReadOnly()
        );
    }

    private void Visit(CodeExpression expression, Alternative alternative)
    {
        if (alternative.Syntax is ThrowExpressionSyntax)
            return;
        if (expression.Conversion.IsUserDefined)
            Stop(expression, ExpressionTraversalReason.UnsupportedExpression);
        else if (alternative.Syntax is ParenthesizedExpressionSyntax parenthesized)
            Follow(expression, alternative, alternative with { Syntax = parenthesized.Expression });
        else if (alternative.Syntax.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            Follow(
                expression,
                alternative,
                alternative with
                {
                    Syntax = ((PostfixUnaryExpressionSyntax)alternative.Syntax).Operand,
                    IsSuppressed = true,
                }
            );
        else if (alternative.Syntax is CheckedExpressionSyntax scoped)
            Follow(expression, alternative, alternative with { Syntax = scoped.Expression });
        else if (!expression.IsResolved)
            Stop(expression, ExpressionTraversalReason.InvalidBinding);
        else
            VisitResolved(expression, alternative);
    }

    private void VisitResolved(CodeExpression expression, Alternative alternative)
    {
        switch (alternative.Syntax)
        {
            case ConditionalExpressionSyntax conditional:
                Follow(
                    expression,
                    alternative,
                    Branch(alternative, conditional.WhenTrue),
                    Branch(alternative, conditional.WhenFalse)
                );
                break;
            case SwitchExpressionSyntax selection:
                Follow(
                    expression,
                    alternative,
                    [.. selection.Arms.Select(arm => Branch(alternative, arm.Expression))]
                );
                break;
            case BinaryExpressionSyntax coalesce
                when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                if (
                    expression.Operation is ICoalesceOperation
                    {
                        ValueConversion.IsUserDefined: true
                    }
                )
                    Stop(expression, ExpressionTraversalReason.UnsupportedExpression);
                else
                    Follow(
                        expression,
                        alternative,
                        Branch(alternative, coalesce.Left) with
                        {
                            Role = ResultValueRole.NonNullLeft,
                        },
                        Branch(alternative, coalesce.Right) with
                        {
                            Role =
                                alternative.Role == ResultValueRole.NonNullLeft
                                    ? ResultValueRole.NonNullLeft
                                    : ResultValueRole.Fallback,
                        }
                    );
                break;
            case CastExpressionSyntax cast
                when expression.Operation
                    is IConversionOperation { Conversion.IsUserDefined: true }:
                Stop(expression, ExpressionTraversalReason.UnsupportedExpression);
                break;
            // A cast to a non-nullable value type yields a value whatever its operand is.
            case CastExpressionSyntax cast when !IsNonNullableValue(expression.Type):
                Follow(
                    expression,
                    alternative,
                    alternative with
                    {
                        Syntax = cast.Expression,
                        IsCast = true,
                    }
                );
                break;
            default:
                Classify(expression, alternative);
                break;
        }
    }

    private static Alternative Branch(Alternative parent, ExpressionSyntax syntax) =>
        parent with
        {
            Syntax = syntax,
            Anchor = syntax,
        };

    private void Classify(CodeExpression expression, Alternative alternative)
    {
        var operation = expression.Operation;
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            if (conversion.Conversion.IsUserDefined)
            {
                Stop(expression, ExpressionTraversalReason.UnsupportedExpression);
                return;
            }
            operation = conversion.Operand;
        }
        if (Kind(expression, operation) is not { } kind)
        {
            Stop(expression, ExpressionTraversalReason.UnsupportedExpression);
            return;
        }
        _values.Add(
            new(
                new(expression.Source, alternative.Anchor),
                expression,
                kind,
                alternative.Role,
                alternative.IsSuppressed,
                alternative.IsCast
            )
        );
    }

    private static ResultValueKind? Kind(CodeExpression expression, IOperation? operation)
    {
        if (
            operation is null or IDynamicInvocationOperation or IDynamicMemberReferenceOperation
            || operation.Type?.TypeKind == TypeKind.Dynamic
        )
            return null;
        if (operation is IDefaultValueOperation)
            return DefaultKind(operation.Type ?? expression.TypeInfo.ConvertedType);
        if (expression.Constant is { HasValue: true } constant)
            return constant.Value is null ? ResultValueKind.Null : ResultValueKind.Constant;
        return operation switch
        {
            IObjectCreationOperation { Arguments.Length: 0 } creation
                when IsNullableValue(creation.Type) => ResultValueKind.Null,
            IObjectCreationOperation
            or ITypeParameterObjectCreationOperation
            or IAnonymousObjectCreationOperation
            or IArrayCreationOperation
            or ICollectionExpressionOperation
            or IDelegateCreationOperation
            or IAnonymousFunctionOperation
            or IWithOperation
            or IInterpolatedStringOperation
            or ITypeOfOperation => ResultValueKind.Construction,
            _ when IsNonNullableValue(operation.Type) => ResultValueKind.ValueTypeValue,
            _ => ResultValueKind.Unproven,
        };
    }

    private static ResultValueKind? DefaultKind(ITypeSymbol? type) =>
        type switch
        {
            null => null,
            _ when IsNullableValue(type) => ResultValueKind.Null,
            { IsValueType: true } => ResultValueKind.ValueTypeDefault,
            { IsReferenceType: true } => ResultValueKind.ReferenceDefault,
            _ => ResultValueKind.TypeParameterDefault,
        };

    private static bool IsNullableValue(ITypeSymbol? type) =>
        type?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static bool IsNonNullableValue(ITypeSymbol? type) =>
        type is { IsValueType: true } && !IsNullableValue(type);

    private void Follow(
        CodeExpression expression,
        Alternative alternative,
        params Alternative[] children
    )
    {
        if (alternative.Depth == maxDepth)
        {
            Stop(expression, ExpressionTraversalReason.DepthLimit);
            return;
        }
        foreach (var child in children.Reverse())
            _pending.Push(child with { Depth = alternative.Depth + 1 });
    }

    private void Stop(CodeExpression expression, ExpressionTraversalReason reason) =>
        _boundaries.Add(new(expression, reason));

    private readonly record struct Alternative(
        ExpressionSyntax Syntax,
        ExpressionSyntax Anchor,
        ResultValueRole Role,
        bool IsSuppressed,
        bool IsCast,
        int Depth
    );
}
