using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Operations;

/// <summary>A named, bound Boolean-check recognizer. Names classify evidence; they do not imply validation policy.</summary>
public sealed class ConditionPattern
{
    private readonly Func<CodeExpression, (CodeExpression Value, bool Outcome)?> _match;

    private ConditionPattern(string name, Func<CodeExpression, (CodeExpression, bool)?> match)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        _match = match;
    }

    /// <summary>The consumer's classification, such as null, empty or whitespace.</summary>
    public string Name { get; }

    /// <summary>Recognizes built-in null/presence checks; the matching outcome always denotes null or missing value.</summary>
    public static ConditionPattern NullTests(string name = "null") =>
        new(
            name,
            expression =>
                NullCheckQueries.Match(expression) is { } check
                    ? (check.CheckedValue, check.Polarity == NullCheckPolarity.IsNull)
                    : null
        );

    /// <summary>Recognizes an exact configured Boolean method and one explicit bound parameter, normalizing ordinary negation and Boolean comparisons.</summary>
    public static ConditionPattern ForCall(
        CodeMember method,
        string valueParameter,
        string name,
        bool matchingOutcome = true
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueParameter);
        return new(
            name,
            expression =>
            {
                if (
                    !expression.IsResolved
                    || Normalize(expression.Operation) is not { } normalized
                    || normalized.Operation is not IInvocationOperation operation
                    || operation.Type?.SpecialType != SpecialType.System_Boolean
                    || !method.Matches(operation.TargetMethod)
                )
                    return null;
                var call = new CodeInvocation(expression.Source, operation);
                var values = call.Parameter(valueParameter)?.Values;
                return values is { Count: 1 } && values[0].Value is { } value
                    ? (value, matchingOutcome != normalized.Negated)
                    : null;
            }
        );
    }

    internal ConditionMatch? Match(CodeCondition condition) =>
        _match(condition.Expression) is { } evidence
            ? new(condition, this, evidence.Value, evidence.Outcome)
            : null;

    private static (IOperation Operation, bool Negated)? Normalize(IOperation? operation)
    {
        var negated = false;
        while (operation is not null)
        {
            if (
                operation is IUnaryOperation
                {
                    OperatorKind: UnaryOperatorKind.Not,
                    OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_Boolean
                } unary
            )
            {
                negated = !negated;
                operation = unary.Operand;
                continue;
            }
            if (
                operation is IBinaryOperation { OperatorMethod: null, IsLifted: false } binary
                && binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
            )
            {
                var left = binary.LeftOperand.ConstantValue;
                var right = binary.RightOperand.ConstantValue;
                var constant = left is { HasValue: true, Value: bool } ? left : right;
                if (constant is { HasValue: true, Value: bool expected })
                {
                    operation = left is { HasValue: true, Value: bool }
                        ? binary.RightOperand
                        : binary.LeftOperand;
                    negated ^= !expected ^ (binary.OperatorKind == BinaryOperatorKind.NotEquals);
                    continue;
                }
            }
            return (operation, negated);
        }
        return null;
    }
}
