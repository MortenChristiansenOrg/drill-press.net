using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Applied-attribute discovery on compiler symbols and declaring-type projection for methods.</summary>
public static class AttributeQueries
{
    /// <summary>Reads only attributes attached to this compiler symbol; does not infer inherited method attributes.</summary>
    public static IEnumerable<CodeAttribute> Attributes(this ISymbol symbol) =>
        symbol.GetAttributes().Select(data => new CodeAttribute(symbol, data));

    /// <summary>Returns distinct immediate declaring source types, consolidating partial declarations only within each owning context.</summary>
    public static CodeQuery<CodeTypeDefinition> ContainingTypes(
        this CodeQuery<CodeMethod> methods
    ) =>
        CodeQuery<CodeTypeDefinition>.Create(solution =>
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
