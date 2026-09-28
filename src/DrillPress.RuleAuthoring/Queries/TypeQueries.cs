using Microsoft.CodeAnalysis;

namespace DrillPress.Queries;

/// <summary>Composable source-anchored static type seeds, bounded traversal and contextual declaration projection.</summary>
public static class TypeQueries
{
    /// <summary>Combines configured seed selectors over selected methods. Each unresolved selected source remains an explicit seed.</summary>
    public static CodeQuery<TypeSeed> TypeSeeds(
        this CodeQuery<CodeMethod> methods,
        params TypeSeedSelector[] selectors
    )
    {
        var configured = selectors.ToArray();
        return methods.SelectMany(method => configured.SelectMany(selector => selector.In(method)));
    }

    /// <summary>Traverses once per source seed/context, visiting constructed symbols before definition deduplication.</summary>
    public static CodeQuery<TypeReachability> TraverseTypes(
        this CodeQuery<TypeSeed> seeds,
        TypeTraversal traversal
    ) => seeds.Select(seed => TypeReachability.Traverse(seed, traversal));

    /// <summary>Filters reached output models without pruning intermediate nodes or discarding incomplete results.</summary>
    public static CodeQuery<TypeReachability> WhereType(
        this CodeQuery<TypeReachability> results,
        Func<ITypeSymbol, bool> where
    ) => results.Select(result => result.WhereType(where));

    /// <summary>Projects selected constructed types to deterministic ordinary-source definitions in compatible dependency contexts. Metadata/generated-only types do not become findings.</summary>
    public static CodeQuery<CodeDeclaration> Declarations(
        this CodeQuery<TypeReachability> results,
        Func<AnalysisProject, bool>? owner = null
    ) =>
        CodeQuery<CodeDeclaration>.Create(solution =>
        {
            var declarations = solution
                .Types.Where(declaration => owner?.Invoke(declaration.Source.Project) ?? true)
                .ToArray();
            var index = declarations
                .SelectMany(declaration =>
                    declaration.Symbol.DeclaringSyntaxReferences.Select(reference =>
                        (reference.SyntaxTree, reference.Span, Declaration: declaration)
                    )
                )
                .ToLookup(entry => (entry.SyntaxTree, entry.Span), entry => entry.Declaration);
            var selected = new HashSet<CodeDeclaration>();
            foreach (var result in results.In(solution))
            foreach (var node in result.Types)
            {
                solution.CancellationToken.ThrowIfCancellationRequested();
                if (node.Type is not INamedTypeSymbol named)
                    continue;
                foreach (var reference in named.OriginalDefinition.DeclaringSyntaxReferences)
                foreach (var declaration in index[(reference.SyntaxTree, reference.Span)])
                    if (
                        solution.ProjectGraph.Includes(
                            result.Seed.Source.Project,
                            declaration.Source.Project
                        )
                    )
                        selected.Add(declaration);
            }
            return declarations.Where(selected.Contains);
        });
}
