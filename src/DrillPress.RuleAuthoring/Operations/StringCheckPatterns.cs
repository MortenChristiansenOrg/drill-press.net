using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Operations;

internal static class StringCheckPatterns
{
    internal static (CodeExpression, bool)? Empty(CodeExpression expression)
    {
        if (
            !expression.IsResolved
            || ConditionPattern.Normalize(expression.Operation) is not { } normalized
        )
            return null;
        var operation = normalized.Operation;
        var positive = !normalized.Negated;
        IOperation? value = null;
        if (
            operation is IBinaryOperation { OperatorMethod: null } binary
            && binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
        )
        {
            positive ^= binary.OperatorKind == BinaryOperatorKind.NotEquals;
            value =
                Compared(binary.LeftOperand, binary.RightOperand)
                ?? Compared(binary.RightOperand, binary.LeftOperand);
        }
        else if (operation is IIsPatternOperation pattern)
        {
            var test = pattern.Pattern;
            while (test is INegatedPatternOperation negated)
            {
                positive = !positive;
                test = negated.Pattern;
            }
            if (
                test is IConstantPatternOperation
                {
                    Value.ConstantValue: { HasValue: true, Value: "" }
                }
            )
                value = pattern.Value;
        }
        return value?.Syntax is ExpressionSyntax syntax
            ? (new(expression.Source, syntax), positive)
            : null;
    }

    private static IOperation? Compared(IOperation value, IOperation constant)
    {
        if (
            (
                constant.ConstantValue is { HasValue: true, Value: "" }
                || constant
                    is IFieldReferenceOperation
                    {
                        Field.Name: "Empty",
                        Field.IsStatic: true,
                        Field.ContainingType.SpecialType: SpecialType.System_String
                    }
            )
            && value.Type?.SpecialType == SpecialType.System_String
        )
            return value;
        return
            constant.ConstantValue is { HasValue: true, Value: 0 }
            && value
                is IPropertyReferenceOperation
                {
                    Property.Name: "Length",
                    Property.ContainingType.SpecialType: SpecialType.System_String,
                    Instance: { } instance
                }
            ? instance
            : null;
    }
}
