using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

internal static class EvaluationTrace
{
    internal static IReadOnlyList<string>? Read(
        IOperation? root,
        IReadOnlyDictionary<TextSpan, int> inputs,
        CancellationToken cancellationToken
    )
    {
        if (root is null)
            return null;
        var result = new List<string>();
        return Visit(root, true) ? result : null;

        bool Visit(IOperation operation, bool isRoot = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inputs.TryGetValue(operation.Syntax.Span, out var input))
            {
                result.Add("input:" + input);
                return true;
            }
            if (isRoot && TransparentOperand(operation) is { } operand)
                return Visit(operand, true);
            if (
                operation
                    is IConditionalOperation
                        or IConditionalAccessOperation
                        or ICoalesceOperation
                        or IAnonymousFunctionOperation
                        or ILocalFunctionOperation
                        or ISwitchExpressionOperation
                        or IAwaitOperation
                        or IBinaryOperation
                        {
                            OperatorKind: BinaryOperatorKind.ConditionalAnd
                                or BinaryOperatorKind.ConditionalOr
                        }
                || operation.Kind == OperationKind.Invalid
            )
                return false;
            foreach (var child in operation.ChildOperations)
                if (!Visit(child))
                    return false;
            if (!isRoot && IsObservable(operation))
                result.Add(
                    operation.Kind
                        + ":"
                        + operation.Syntax.WithoutTrivia().NormalizeWhitespace().ToString()
                        + ":"
                        + Target(operation)
                );
            return true;
        }
    }

    internal static IOperation? UnwrapBooleanNegation(IOperation? operation)
    {
        while (TransparentOperand(operation) is { } operand)
            operation = operand;
        return operation;
    }

    private static IOperation? TransparentOperand(IOperation? operation) =>
        operation switch
        {
            IParenthesizedOperation parentheses => parentheses.Operand,
            IUnaryOperation
            {
                OperatorKind: UnaryOperatorKind.Not,
                OperatorMethod: null,
                Type.SpecialType: SpecialType.System_Boolean
            } negation => negation.Operand,
            _ => null,
        };

    internal static TextSpan OperandSpan(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;
        return expression.Span;
    }

    private static bool IsObservable(IOperation operation) =>
        !operation.ConstantValue.HasValue
        && operation
            is IInvocationOperation
                or IObjectCreationOperation
                or IArrayCreationOperation
                or ILocalReferenceOperation
                or IParameterReferenceOperation
                or IFieldReferenceOperation
                or IInstanceReferenceOperation
                or ILiteralOperation
                or IPropertyReferenceOperation
                or IAssignmentOperation
                or IIncrementOrDecrementOperation
                or IThrowOperation
                or IConversionOperation { OperatorMethod: not null }
                or IBinaryOperation { OperatorMethod: not null }
                or IUnaryOperation { OperatorMethod: not null };

    private static string? Target(IOperation operation) =>
        RewriteSymbols.Identity(
            operation switch
            {
                IInvocationOperation call => call.TargetMethod,
                IObjectCreationOperation creation => creation.Constructor,
                IPropertyReferenceOperation property => property.Property,
                IConversionOperation conversion => conversion.OperatorMethod,
                IBinaryOperation binary => binary.OperatorMethod,
                IUnaryOperation unary => unary.OperatorMethod,
                _ => null,
            }
        );
}
