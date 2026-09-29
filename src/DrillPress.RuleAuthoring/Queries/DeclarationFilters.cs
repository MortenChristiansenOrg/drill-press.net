namespace DrillPress;

/// <summary>Readable method names, class ancestry and interface scopes.</summary>
public static class DeclarationFilters
{
    /// <summary>Selects exact ordinal method names, retaining unresolved declarations.</summary>
    public static CodeQuery<CodeMethod> Named(
        this CodeQuery<CodeMethod> methods,
        params string[] names
    )
    {
        foreach (var name in names)
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var selected = names.ToHashSet();
        return methods.Where(method => selected.Contains(method.Name));
    }

    /// <summary>Selects methods whose declaring type strictly derives from any supplied class, including constructed open-generic bases.</summary>
    public static CodeQuery<CodeMethod> DeclaredInTypesDerivedFrom(
        this CodeQuery<CodeMethod> methods,
        params CodeType[] types
    )
    {
        var selected = types.ToArray();
        return methods.Where(method =>
            method.Symbol?.ContainingType is { } owner && selected.Any(owner.DerivesFrom)
        );
    }

    /// <summary>Selects strict class descendants; the supplied base definitions themselves are excluded.</summary>
    public static CodeQuery<CodeDeclaration> DerivedFrom(
        this CodeQuery<CodeDeclaration> types,
        params CodeType[] bases
    )
    {
        var selected = bases.ToArray();
        return types.Where(type => selected.Any(type.DerivesFrom));
    }

    /// <summary>Selects definitions implementing the supplied interface, including inherited and constructed interfaces.</summary>
    public static CodeQuery<CodeDeclaration> ImplementingInterface(
        this CodeQuery<CodeDeclaration> types,
        CodeType contract
    ) => types.Where(type => type.Implements(contract));
}
