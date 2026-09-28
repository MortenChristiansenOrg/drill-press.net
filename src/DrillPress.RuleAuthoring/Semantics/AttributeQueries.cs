using Microsoft.CodeAnalysis;

namespace DrillPress.Semantics;

/// <summary>Declared-attribute discovery and distinct declaring-type projection, independent of framework discovery policies.</summary>
public static class AttributeQueries
{
    /// <summary>Reads only attributes attached to this compiler symbol; does not infer inherited method attributes.</summary>
    public static IEnumerable<CodeAttribute> Attributes(this ISymbol symbol) =>
        symbol.GetAttributes().Select(data => new CodeAttribute(symbol, data));

    /// <summary>Selects methods bearing any configured direct or derived marker attribute.</summary>
    public static CodeQuery<CodeMethod> WithAttribute(
        this CodeQuery<CodeMethod> methods,
        params CodeType[] markers
    )
    {
        var captured = markers.ToArray();
        return methods.Where(method =>
            method.Symbol is { } symbol
            && symbol
                .Attributes()
                .Any(attribute => captured.Any(marker => attribute.Matches(marker)))
        );
    }

    /// <summary>Selects named types bearing a configured direct or derived marker attribute.</summary>
    public static CodeQuery<CodeDeclaration> WithAttribute(
        this CodeQuery<CodeDeclaration> types,
        params CodeType[] markers
    )
    {
        var captured = markers.ToArray();
        return types.Where(type =>
            type.Symbol.Attributes()
                .Any(attribute => captured.Any(marker => attribute.Matches(marker)))
        );
    }

    /// <summary>Selects attributed symbols of any declaration kind.</summary>
    public static CodeQuery<CodeSymbol> WithAttribute(
        this CodeQuery<CodeSymbol> symbols,
        params CodeType[] markers
    )
    {
        var captured = markers.ToArray();
        return symbols.Where(symbol =>
            symbol
                .Symbol.Attributes()
                .Any(attribute => captured.Any(marker => attribute.Matches(marker)))
        );
    }

    /// <summary>Returns distinct immediate declaring source types, consolidating partial declarations only within each owning context.</summary>
    public static CodeQuery<CodeDeclaration> ContainingTypes(this CodeQuery<CodeMethod> methods) =>
        CodeQuery<CodeDeclaration>.Create(solution =>
        {
            var owners = methods
                .In(solution)
                .Where(method => method.Symbol is not null)
                .GroupBy(method => method.Source.Project)
                .ToDictionary(
                    group => group.Key,
                    group =>
                        group
                            .Select(method => method.Symbol!.ContainingType)
                            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                );
            return solution.Types.Where(type =>
                owners.TryGetValue(type.Source.Project, out var types)
                && types.Contains(type.Symbol)
            );
        });
}
