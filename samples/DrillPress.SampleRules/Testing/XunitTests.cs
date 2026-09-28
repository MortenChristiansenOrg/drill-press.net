using Microsoft.CodeAnalysis;

namespace DrillPress.SampleRules.Testing;

/// <summary>Reusable semantic xUnit selection shared by method-level conventions.</summary>
internal static class XunitTests
{
    private static readonly CodeType[] _markers = (
        from assembly in new[] { "xunit.core", "xunit.v3.core", "xunit.assert", "xunit.v3.assert" }
        from name in new[] { "Xunit.FactAttribute", "Xunit.TheoryAttribute" }
        select CodeType.Named(name, assembly)
    ).ToArray();

    /// <summary>Selects methods with genuine xUnit Fact/Theory attributes, including derived attributes.</summary>
    public static RuleCondition<CodeMethod> AreTests { get; } =
        new(method =>
            method
                .Symbol?.Attributes()
                .Any(attribute => _markers.Any(marker => attribute.Matches(marker))) == true
        );

    /// <summary>One reusable query for all xUnit method conventions.</summary>
    public static CodeQuery<CodeMethod> Methods { get; } = Code.Methods.WithAttribute(_markers);

    internal static bool IsXunitType(INamedTypeSymbol type, string metadataName) =>
        CodeType.Named(metadataName).Matches(type)
        && type.ContainingAssembly.Name
            is "xunit.core"
                or "xunit.v3.core"
                or "xunit.assert"
                or "xunit.v3.assert";
}
