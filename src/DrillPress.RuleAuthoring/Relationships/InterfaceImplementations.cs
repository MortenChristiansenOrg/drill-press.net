using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Relationships;

/// <summary>Discovers source implementations separately within compatible evaluated source graphs.</summary>
public sealed class InterfaceImplementations(AnalysisSolution solution)
{
    private readonly Dictionary<string, IReadOnlyList<INamedTypeSymbol>> _definitions = [];
    private readonly CompilationViews _views = new(solution);
    private long _definitionComparisons;
    private readonly Dictionary<
        string,
        Dictionary<ISymbol, Dictionary<string, INamedTypeSymbol>>
    > _indexes = [];
    private readonly Dictionary<
        CodeDeclaration,
        IReadOnlyList<InterfaceImplementationView>
    > _matches = [];
    private long _indexEntries;
    private long _indexLookups;

    internal void WriteProfileCounters()
    {
        solution.Options.Profile.Count("interface.definition.comparisons", _definitionComparisons);
        if (solution.Options.EnableOptimizations)
        {
            solution.Options.Profile.Count("interface.index.entries", _indexEntries);
            solution.Options.Profile.Count("interface.index.lookups", _indexLookups);
        }
    }

    /// <summary>Returns implementations in each maximal compatible view, including test projects and abstract types. Partial and constructed generic occurrences count once per source definition and evaluated context.</summary>
    /// <remarks>Consumers choose project scope, concrete-type filters and cardinality. Alternate frameworks are kept separate; generated implementations participate. Only loaded source definitions are returned, not external metadata consumers.</remarks>
    public IReadOnlyList<InterfaceImplementationView> In(CodeDeclaration declaration)
    {
        solution.CancellationToken.ThrowIfCancellationRequested();
        if (!_matches.TryGetValue(declaration, out var matches))
        {
            matches = Array.AsReadOnly(
                _views
                    .For(declaration.Source.Project)
                    .Select(view => new InterfaceImplementationView(
                        Array.AsReadOnly(view),
                        FindInView(view, declaration)
                    ))
                    .ToArray()
            );
            _matches.Add(declaration, matches);
        }
        return matches;
    }

    private IReadOnlyList<InterfaceImplementation> FindInView(
        AnalysisProject[] projects,
        CodeDeclaration declaration
    )
    {
        var implementations = new Dictionary<string, InterfaceImplementation>();
        foreach (var project in projects)
        {
            if (!_views.Reaches(project, declaration.Source.Project.Snapshot.ContextId))
            {
                continue;
            }

            var assembly = ResolveAssembly(project, declaration.Source.Project);
            var target = assembly?.GetTypeByMetadataName(
                CodeType.MetadataNameOf(declaration.Symbol)
            );
            if (target is null)
            {
                continue;
            }

            if (solution.Options.EnableOptimizations)
            {
                _indexLookups++;
                if (Index(project).TryGetValue(target, out var matches))
                {
                    foreach (var (identity, symbol) in matches)
                    {
                        implementations.TryAdd(identity, new(project, symbol));
                    }
                }

                continue;
            }

            foreach (var type in Definitions(project))
            {
                _definitionComparisons++;
                if (
                    type.AllInterfaces.Any(contract =>
                        SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, target)
                    )
                )
                {
                    implementations.TryAdd(
                        project.Snapshot.ContextId + ":" + CodeType.MetadataNameOf(type),
                        new(project, type)
                    );
                }
            }
        }

        return Array.AsReadOnly(implementations.Values.ToArray());
    }

    private Dictionary<ISymbol, Dictionary<string, INamedTypeSymbol>> Index(AnalysisProject project)
    {
        if (_indexes.TryGetValue(project.Snapshot.ContextId, out var index))
        {
            return index;
        }

        index = new(SymbolEqualityComparer.Default);
        foreach (var type in Definitions(project))
        {
            solution.CancellationToken.ThrowIfCancellationRequested();
            var identity = project.Snapshot.ContextId + ":" + CodeType.MetadataNameOf(type);
            foreach (var contract in type.AllInterfaces)
            {
                if (!index.TryGetValue(contract.OriginalDefinition, out var implementations))
                {
                    implementations = [];
                    index.Add(contract.OriginalDefinition, implementations);
                }

                if (implementations.TryAdd(identity, type))
                {
                    _indexEntries++;
                }
            }
        }

        _indexes.Add(project.Snapshot.ContextId, index);
        return index;
    }

    private static IAssemblySymbol? ResolveAssembly(AnalysisProject project, AnalysisProject owner)
    {
        if (project == owner)
        {
            return project.Compilation.Assembly;
        }

        // Compilation references identify the actual source context, including aliases and equal assembly names.
        foreach (var reference in project.Compilation.References.OfType<CompilationReference>())
        {
            if (ReferenceEquals(reference.Compilation, owner.Compilation))
            {
                return project.Compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
            }
        }

        return project.Compilation.SourceModule.ReferencedAssemblySymbols.SingleOrDefault(
            assembly => assembly.Identity.Equals(owner.Compilation.Assembly.Identity)
        );
    }

    private IReadOnlyList<INamedTypeSymbol> Definitions(AnalysisProject project)
    {
        if (!_definitions.TryGetValue(project.Snapshot.ContextId, out var definitions))
        {
            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            definitions = project
                .Sources.SelectMany(source =>
                    source
                        .Tree.GetRoot(source.Project.CancellationToken)
                        .DescendantNodes()
                        .OfType<TypeDeclarationSyntax>()
                        .Select(syntax =>
                            source.Model.GetDeclaredSymbol(syntax, source.Project.CancellationToken)
                        )
                )
                .OfType<INamedTypeSymbol>()
                .Where(type => seen.Add(type))
                .ToArray();
            _definitions.Add(project.Snapshot.ContextId, definitions);
        }

        return definitions;
    }
}
