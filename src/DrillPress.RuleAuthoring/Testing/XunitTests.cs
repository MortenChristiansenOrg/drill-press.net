using Microsoft.CodeAnalysis;

namespace DrillPress.Testing;

/// <summary>Reusable semantic xUnit selection shared by method-level conventions.</summary>
public static class XunitTests
{
    /// <summary>Selects methods with genuine xUnit Fact/Theory attributes, including derived attributes.</summary>
    public static RuleCondition<CodeMethod> AreTests { get; } =
        new(method =>
            method
                .Symbol?.GetAttributes()
                .Any(attribute => IsTestAttribute(attribute.AttributeClass)) == true
        );

    /// <summary>One reusable query for all xUnit method conventions.</summary>
    public static CodeQuery<CodeMethod> Methods { get; } = Code.Methods.Where(AreTests);

    internal static bool IsXunitType(INamedTypeSymbol type, string metadataName) =>
        CodeType.MetadataNameOf(type) == metadataName
        && type.ContainingAssembly.Name
            is "xunit.core"
                or "xunit.v3.core"
                or "xunit.assert"
                or "xunit.v3.assert";

    private static bool IsTestAttribute(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (
                IsXunitType(current, "Xunit.FactAttribute")
                || IsXunitType(current, "Xunit.TheoryAttribute")
            )
            {
                return true;
            }
        }

        return false;
    }
}
