using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Operations;

/// <summary>Compiler-bound null-check recognition without deleting conditions or inferring runtime non-nullness.</summary>
public static class NullCheckQueries
{
    /// <summary>Normalizes checks within selected executable scopes.</summary>
    public static CodeQuery<CodeNullCheck> NullChecks(this CodeQuery<CodeBody> bodies) =>
        In(bodies.Nodes<ExpressionSyntax>());

    /// <summary>Recognizes checks in a syntax selection, returning only the outermost supported negation/parenthesis shape per occurrence.</summary>
    public static CodeQuery<CodeNullCheck> In(CodeQuery<CodeNode<ExpressionSyntax>> expressions) =>
        CodeQuery<CodeNullCheck>.Create(solution =>
        {
            var nodes = expressions.In(solution);
            var matches = nodes
                .Select(node => Match(new(node.Source, node.Syntax)))
                .OfType<CodeNullCheck>()
                .ToArray();
            return matches
                .GroupBy(check => (check.Source, check.CheckedValue.Syntax.Span))
                .Select(group => group.MaxBy(check => check.Condition.Syntax.Span.Length)!);
        });

    /// <summary>Recognizes a single complete Boolean check; unsupported custom operators and invalid bindings return no match.</summary>
    public static CodeNullCheck? Match(CodeExpression condition)
    {
        if (!condition.IsResolved || condition.Operation is not { } operation)
            return null;
        var negated = false;
        while (
            operation
                is IUnaryOperation
                {
                    OperatorKind: UnaryOperatorKind.Not,
                    OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_Boolean
                } unary
        )
        {
            negated = !negated;
            operation = unary.Operand;
        }
        var match = Operand(operation);
        if (match is not { } found || found.Value.Syntax is not ExpressionSyntax syntax)
            return null;
        var type = found.Value.Type;
        var domain = type
            is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
            ? NullCheckDomain.NullableValue
            : NullCheckDomain.Reference;
        return new(
            condition,
            new(condition.Source, syntax),
            found.NotNull != negated ? NullCheckPolarity.IsNotNull : NullCheckPolarity.IsNull,
            domain
        );
    }

    private static (IOperation Value, bool NotNull)? Operand(IOperation operation)
    {
        if (operation is IIsPatternOperation pattern)
        {
            var current = pattern.Pattern;
            var notNull = false;
            while (current is INegatedPatternOperation negated)
            {
                notNull = !notNull;
                current = negated.Pattern;
            }
            return
                current
                    is IConstantPatternOperation
                    {
                        Value.ConstantValue: { HasValue: true, Value: null }
                    }
                ? (pattern.Value, notNull)
                : null;
        }
        if (
            operation is IBinaryOperation { OperatorMethod: null } binary
            && binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
        )
        {
            if (!binary.IsLifted)
            {
                var boolean =
                    binary.LeftOperand.ConstantValue is { HasValue: true, Value: bool left }
                        ? (bool?)left
                    : binary.RightOperand.ConstantValue is { HasValue: true, Value: bool right }
                        ? right
                    : null;
                var other = binary.LeftOperand.ConstantValue is { HasValue: true, Value: bool }
                    ? binary.RightOperand
                    : binary.LeftOperand;
                if (boolean is { } expected && Operand(other) is { } inner)
                    return (
                        inner.Value,
                        inner.NotNull
                            ^ (!expected ^ (binary.OperatorKind == BinaryOperatorKind.NotEquals))
                    );
            }
            var value =
                IsNull(binary.LeftOperand) ? binary.RightOperand
                : IsNull(binary.RightOperand) ? binary.LeftOperand
                : null;
            if (value is null)
                return null;
            if (value is IConversionOperation { Conversion.IsUserDefined: true })
                return null;
            while (value is IConversionOperation { Conversion.IsIdentity: true } conversion)
                value = conversion.Operand;
            return (value, binary.OperatorKind == BinaryOperatorKind.NotEquals);
        }
        if (
            operation is IPropertyReferenceOperation
            {
                Property.Name: "HasValue",
                Property
                    .ContainingType
                    .OriginalDefinition
                    .SpecialType: SpecialType.System_Nullable_T,
                Instance: { } instance
            }
        )
            return (instance, true);
        return null;
    }

    private static bool IsNull(IOperation value) =>
        value.ConstantValue is { HasValue: true, Value: null };
}
