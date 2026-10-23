using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Semantic predicates that avoid spelling-based matches and reject unresolved types.</summary>
public static class Symbols
{
    /// <summary>Tests whether a member is declared in the selected source type or its class ancestry, retaining constructed type and assembly identity across rewritten contexts.</summary>
    public static bool IsDeclaredInOrAbove(this ISymbol member, CodeTypeDefinition owner)
    {
        for (var type = owner.Symbol; type is not null; type = type.BaseType)
            if (
                member.ContainingType is { } containing
                && CodeType.FromSymbol(type).Matches(containing)
            )
                return true;
        return false;
    }

    /// <summary>Tests resolved attributes, optionally accepting derived attribute classes.</summary>
    public static bool HasAttribute(
        this ISymbol symbol,
        CodeType attribute,
        bool includeDerived = true
    ) =>
        symbol
            .GetAttributes()
            .Any(data =>
                data.AttributeClass is { } type
                && (includeDerived ? IsOrDerivesFrom(type, attribute) : attribute.Matches(type))
            );

    /// <summary>Tests the named type and its base-class chain.</summary>
    public static bool IsOrDerivesFrom(this INamedTypeSymbol type, CodeType target)
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

    /// <summary>Tests the strict base-class chain, excluding the type itself.</summary>
    public static bool DerivesFrom(this INamedTypeSymbol type, CodeType target) =>
        type.BaseType is { } parent && parent.IsOrDerivesFrom(target);

    /// <summary>Matches an applied marker and a predicate over its compiler-recorded values.</summary>
    public static bool HasAttribute(
        this ISymbol symbol,
        CodeType marker,
        Func<CodeAttribute, bool> where
    ) => symbol.Attributes().Any(attribute => attribute.Matches(marker) && where(attribute));

    /// <summary>Tests all implemented interfaces, including inherited and constructed interfaces.</summary>
    public static bool Implements(INamedTypeSymbol type, CodeType contract) =>
        type.AllInterfaces.Any(contract.Matches);

    /// <summary>Whether the method overrides a base member or implements an interface member, explicitly or implicitly, so another declaration decides its signature and parameter names.</summary>
    public static bool ImplementsOrOverrides(this IMethodSymbol method)
    {
        if (method.IsOverride || method.ExplicitInterfaceImplementations.Length > 0)
            return true;
        var type = method.ContainingType;
        return type is not null
            && type.AllInterfaces.Any(contract =>
                contract
                    .GetMembers(method.Name)
                    .OfType<IMethodSymbol>()
                    .Any(member =>
                        SymbolEqualityComparer.Default.Equals(
                            type.FindImplementationForInterfaceMember(member),
                            method
                        )
                    )
            );
    }

    /// <summary>Whether the method has the event-handler signature: an <c>object</c> sender followed by <c>EventArgs</c> or a type derived from it.</summary>
    public static bool IsEventHandler(this IMethodSymbol method) =>
        method.Parameters
            is [
                { Type.SpecialType: SpecialType.System_Object },
                { Type: INamedTypeSymbol arguments },
            ]
        && arguments.IsOrDerivesFrom(CodeType.Of<EventArgs>());
}
