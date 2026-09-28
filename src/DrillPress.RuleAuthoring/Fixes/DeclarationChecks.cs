using Microsoft.CodeAnalysis;

namespace DrillPress.Fixes;

/// <summary>Separate declaration contracts. Equal identity/accessibility alone never proves arbitrary modifier-removal behavior.</summary>
public static class DeclarationChecks
{
    /// <summary>Compares compiler-declared accessibility for every affected symbol, including merged partial declarations.</summary>
    public static ProofResult SameDeclaredAccessibility(DeclarationRewrite change) =>
        change.Symbols.All(pair =>
            pair.Before.DeclaredAccessibility == pair.After.DeclaredAccessibility
        )
            ? ProofResult.Proven
            : ProofResult.Disproven;

    /// <summary>Compares contextual declaration correspondence, assembly/kind/name/signature and partial-method pairing.</summary>
    public static ProofResult SameIdentity(DeclarationRewrite change) =>
        change.Symbols.All(pair =>
            RewriteSymbols.Same(pair.Before, pair.After, change.Rewrite.Context)
            && Pairing(pair.Before) == Pairing(pair.After)
        )
            ? ProofResult.Proven
            : ProofResult.Disproven;

    /// <summary>Compares accessibility at each containing declaration as well as the selected symbol; it cannot hide a changed declared contract.</summary>
    public static ProofResult SameContainingAccessibility(DeclarationRewrite change) =>
        change.Symbols.All(pair => AccessChain(pair.Before).SequenceEqual(AccessChain(pair.After)))
            ? ProofResult.Proven
            : ProofResult.Disproven;

    /// <summary>Preserves compiler modifier facts for every declared symbol; additional consumer proof is still required for body/reflection/ABI policy.</summary>
    public static ProofResult SameContract(DeclarationRewrite change) =>
        change.Symbols.All(pair => Contract(pair.Before) == Contract(pair.After))
            ? ProofResult.Proven
            : ProofResult.Disproven;

    private static IEnumerable<Accessibility> AccessChain(ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
            yield return current.DeclaredAccessibility;
    }

    private static (bool, bool) Pairing(ISymbol symbol) =>
        symbol is IMethodSymbol method
            ? (
                method.PartialDefinitionPart is not null,
                method.PartialImplementationPart is not null
            )
            : default;

    private static string Contract(ISymbol symbol) =>
        $"{symbol.IsStatic}:{symbol.IsAbstract}:{symbol.IsVirtual}:{symbol.IsOverride}:{symbol.IsSealed}:{symbol.IsExtern}:"
        + (
            symbol switch
            {
                IMethodSymbol method =>
                    $"{method.IsAsync}:{method.IsReadOnly}:{method.IsExtensionMethod}:{method.ReturnsByRef}:{method.ReturnsByRefReadonly}",
                IFieldSymbol field =>
                    $"{field.IsReadOnly}:{field.IsVolatile}:{field.IsConst}:{field.IsRequired}",
                IPropertySymbol property =>
                    $"{property.IsReadOnly}:{property.IsRequired}:{property.ReturnsByRef}:{property.ReturnsByRefReadonly}",
                INamedTypeSymbol type => $"{type.IsReadOnly}:{type.IsRefLikeType}:{type.IsRecord}",
                _ => "",
            }
        );
}
