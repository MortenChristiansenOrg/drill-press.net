using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>Member keys selected by fluent query arguments, without assuming a LINQ provider or query execution.</summary>
public static class QueryKeys
{
    /// <summary>Reads member comparisons against non-constant values in call arguments. Greater/greater-or-equal point forward, less/less-or-equal backward. Compare/CompareTo against zero are normalized by their written call shape. This is syntactic cursor evidence, not proof of pagination or operator semantics.</summary>
    public static IEnumerable<(string Field, bool After)> ComparedMembers(IInvocationOperation call)
    {
        return call
            .Arguments.SelectMany(argument => argument.Value.DescendantsAndSelf())
            .OfType<IBinaryOperation>()
            .SelectMany(binary =>
            {
                var greater =
                    binary.OperatorKind
                    is BinaryOperatorKind.GreaterThan
                        or BinaryOperatorKind.GreaterThanOrEqual;
                var less =
                    binary.OperatorKind
                    is BinaryOperatorKind.LessThan
                        or BinaryOperatorKind.LessThanOrEqual;
                if (!greater && !less)
                {
                    return [];
                }
                var (left, right) = (Unwrap(binary.LeftOperand), Unwrap(binary.RightOperand));
                // Strings compare as x.Name.CompareTo(last) > 0 or string.Compare(x.Name, last) > 0.
                if (
                    right.ConstantValue is { HasValue: true, Value: 0 }
                    && Compared(left) is { } comparedLeft
                )
                {
                    (left, right) = comparedLeft;
                }
                else if (
                    left.ConstantValue is { HasValue: true, Value: 0 }
                    && Compared(right) is { } comparedRight
                )
                {
                    (left, right) = comparedRight;
                    (greater, less) = (less, greater);
                }
                if (left.ConstantValue.HasValue || right.ConstantValue.HasValue)
                {
                    return [];
                }
                var cursors = new List<(string, bool)>();
                if (MemberName(left) is { } leftField)
                {
                    cursors.Add((leftField, greater));
                }
                if (MemberName(right) is { } rightField)
                {
                    cursors.Add((rightField, less));
                }
                return cursors;
            });
    }

    private static (IOperation Left, IOperation Right)? Compared(IOperation operation) =>
        operation switch
        {
            IInvocationOperation
            {
                TargetMethod.Name: "CompareTo",
                Instance: { } instance,
                Arguments: [var other]
            } => (Unwrap(instance), Unwrap(other.Value)),
            IInvocationOperation
            {
                TargetMethod.Name: "Compare",
                TargetMethod.IsStatic: true,
                Arguments: [var first, var second, ..]
            } => (Unwrap(first.Value), Unwrap(second.Value)),
            _ => null,
        };

    /// <summary>The index field an ordering or filter call names, by a lambda such as <c>x =&gt; x.Cvr</c> or a constant string.</summary>
    public static string? SelectedMemberName(IInvocationOperation call)
    {
        foreach (var argument in call.Arguments)
        {
            if (argument.Parameter?.Ordinal == 0 && call.TargetMethod.IsExtensionMethod)
            {
                continue;
            }
            var value = Unwrap(argument.Value);
            if (value.ConstantValue is { HasValue: true, Value: string text })
            {
                return text;
            }
            if (value is IAnonymousFunctionOperation lambda)
            {
                var returned = lambda
                    .Body.Operations.OfType<IReturnOperation>()
                    .FirstOrDefault()
                    ?.ReturnedValue;
                return returned is null ? null : MemberName(returned);
            }
        }
        return null;
    }

    private static string? MemberName(IOperation operation) =>
        Unwrap(operation) switch
        {
            IPropertyReferenceOperation property => property.Property.Name,
            IFieldReferenceOperation field => field.Field.Name,
            _ => null,
        };

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    break;
                case IDelegateCreationOperation creation:
                    operation = creation.Target;
                    break;
                default:
                    return operation;
            }
        }
    }
}
