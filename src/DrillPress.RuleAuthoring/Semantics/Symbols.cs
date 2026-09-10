using Microsoft.CodeAnalysis;

namespace DrillPress.Semantics;

/// <summary>Semantic predicates that avoid spelling-based matches and reject unresolved types.</summary>
public static class Symbols
{
    /// <summary>Tests resolved attributes, optionally accepting derived attribute classes.</summary>
    public static bool HasAttribute(ISymbol symbol, CodeType attribute, bool includeDerived = true) =>
        symbol.GetAttributes().Any(data => data.AttributeClass is { } type &&
            (includeDerived ? IsOrDerivesFrom(type, attribute) : attribute.Matches(type)));

    /// <summary>Tests the named type and its base-class chain.</summary>
    public static bool IsOrDerivesFrom(INamedTypeSymbol type, CodeType target)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.TypeKind != TypeKind.Error && target.Matches(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tests all implemented interfaces, including inherited and constructed interfaces.</summary>
    public static bool Implements(INamedTypeSymbol type, CodeType contract) => type.AllInterfaces.Any(contract.Matches);
}
