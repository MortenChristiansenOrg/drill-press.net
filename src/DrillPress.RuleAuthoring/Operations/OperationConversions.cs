using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>Views of compiler operations without the conversions around them.</summary>
public static class OperationConversions
{
    /// <summary>The operand inside conversion operations, such as the value an argument converts to its parameter type. Explicit conversions (casts) remain unless requested; parentheses remain.</summary>
    [return: NotNullIfNotNull(nameof(operation))]
    public static IOperation? WithoutConversions(
        this IOperation? operation,
        bool includeExplicit = false
    )
    {
        while (
            operation is IConversionOperation conversion
            && (includeExplicit || conversion.IsImplicit)
        )
            operation = conversion.Operand;
        return operation;
    }
}
