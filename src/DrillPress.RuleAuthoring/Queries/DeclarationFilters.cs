namespace DrillPress;

/// <summary>Class ancestry and interface filters for types and methods.</summary>
public static class DeclarationFilters
{
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
    public static CodeQuery<CodeTypeDefinition> DerivedFrom(
        this CodeQuery<CodeTypeDefinition> types,
        params CodeType[] bases
    )
    {
        var selected = bases.ToArray();
        return types.Where(type => selected.Any(type.DerivesFrom));
    }

    /// <summary>Selects definitions implementing the supplied interface, including inherited and constructed interfaces.</summary>
    public static CodeQuery<CodeTypeDefinition> ImplementingInterface(
        this CodeQuery<CodeTypeDefinition> types,
        CodeType contract
    ) => types.Where(type => type.Implements(contract));
}
