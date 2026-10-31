using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

internal sealed class PropertyViewProjection(AnalysisSolution solution, PropertyViewOptions options)
{
    private static readonly object _sourcesKey = new();
    private static readonly SymbolDisplayFormat _identity =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    private readonly ILookup<SyntaxTree, AnalysisSource> _sources = solution.Cached(
        _sourcesKey,
        () => solution.Projects.SelectMany(project => project.Sources).ToLookup(s => s.Tree)
    );

    internal IEnumerable<CodePropertyView> Project(IEnumerable<PropertyViewInput> inputs)
    {
        var entries = new Dictionary<Key, Entry>();
        foreach (var input in inputs)
        {
            solution.CancellationToken.ThrowIfCancellationRequested();
            foreach (var property in Selected(input.Type))
            {
                if (Anchor(property, input.Context) is not { } anchor)
                    continue;
                var key = new Key(
                    anchor.Source,
                    anchor.Syntax.Span.Start,
                    property.ContainingType.ToDisplayString(_identity),
                    property.ToDisplayString(_identity),
                    property.Type.ToDisplayString(_identity),
                    property.NullableAnnotation
                );
                if (!entries.TryGetValue(key, out var entry))
                    entries.Add(key, entry = new(anchor, property, input.Context, []));
                if (input.Reachability is { } reached && !entry.ReachedFrom.Contains(reached))
                    entry.ReachedFrom.Add(reached);
            }
        }
        return entries
            .OrderBy(pair => pair.Key.Source.Document.Path, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Source.Project.Name, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Start)
            .ThenBy(pair => pair.Key.Owner, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Type, StringComparer.Ordinal)
            .Select(pair => new CodePropertyView(
                pair.Value.Anchor.Source,
                pair.Value.Anchor.Syntax,
                pair.Value.Property,
                Default(pair.Value.Property, pair.Value.Anchor, pair.Value.Context),
                pair.Value.ReachedFrom.AsReadOnly()
            ))
            .ToArray();
    }

    // Nearest declarations first, so a hidden or overridden base property yields to the visible one.
    private IEnumerable<IPropertySymbol> Selected(ITypeSymbol type)
    {
        var names = new HashSet<string>();
        var overridden = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var property in TypeTraversal.Properties(type, options.Inherited))
        {
            if (
                !IsAccessible(property)
                || property.IsStatic && !options.IncludeStatic
                || property.IsIndexer && !options.IncludeIndexers
                || overridden.Contains(property)
            )
                continue;
            if (property.OverriddenProperty is { } hidden)
                overridden.Add(hidden);
            if (
                !property.IsIndexer
                && type.TypeKind != TypeKind.Interface
                && !names.Add(property.Name)
            )
                continue;
            if (options.Where?.Invoke(property) ?? true)
                yield return property;
        }
    }

    private bool IsAccessible(IPropertySymbol property) =>
        options.IncludeNonPublic
        || property.DeclaredAccessibility == Accessibility.Public
            && property.GetMethod?.DeclaredAccessibility == Accessibility.Public;

    private (AnalysisSource Source, SyntaxNode Syntax)? Anchor(
        IPropertySymbol property,
        AnalysisProject context
    )
    {
        foreach (var (source, syntax) in Declarations(property, context))
            if (
                (options.IncludeGenerated || !source.Document.IsGenerated)
                && (options.Owner?.Invoke(source.Project) ?? true)
            )
                return (source, syntax);
        return null;
    }

    private PropertyDefault? Default(
        IPropertySymbol property,
        (AnalysisSource Source, SyntaxNode Syntax) anchor,
        AnalysisProject context
    )
    {
        if (anchor.Syntax is ParameterSyntax { Default.Value: { } value })
            return new(PropertyDefaultKind.PositionalParameterDefault, new(anchor.Source, value));
        foreach (var (source, syntax) in Declarations(property, context))
            if (syntax is PropertyDeclarationSyntax { Initializer.Value: { } initializer })
                return new(PropertyDefaultKind.PropertyInitializer, new(source, initializer));
        return null;
    }

    private IEnumerable<(AnalysisSource Source, SyntaxNode Syntax)> Declarations(
        IPropertySymbol property,
        AnalysisProject context
    )
    {
        var definition = property.OriginalDefinition;
        var references = new[]
        {
            definition,
            definition.PartialImplementationPart,
            definition.PartialDefinitionPart,
        }
            .OfType<IPropertySymbol>()
            .SelectMany(part => part.DeclaringSyntaxReferences);
        foreach (var reference in references)
        {
            var syntax = reference.GetSyntax(solution.CancellationToken);
            if (
                syntax
                is not (PropertyDeclarationSyntax or IndexerDeclarationSyntax or ParameterSyntax)
            )
                continue;
            foreach (var source in _sources[reference.SyntaxTree])
                if (solution.ProjectGraph.Includes(context, source.Project))
                    yield return (source, syntax);
        }
    }

    private sealed record Key(
        AnalysisSource Source,
        int Start,
        string Owner,
        string Property,
        string Type,
        NullableAnnotation Annotation
    );

    private sealed record Entry(
        (AnalysisSource Source, SyntaxNode Syntax) Anchor,
        IPropertySymbol Property,
        AnalysisProject Context,
        List<TypeReachability> ReachedFrom
    );
}
