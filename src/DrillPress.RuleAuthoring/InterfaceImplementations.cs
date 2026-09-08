using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Counts concrete source definitions within compatible evaluated source graphs.</summary>
public sealed class InterfaceImplementations(AnalysisSolution solution)
{
    private readonly Dictionary<string, IReadOnlyList<INamedTypeSymbol>> _definitions = [];
    private readonly CompilationViews _views = new(solution);
    private long _definitionComparisons;
    private readonly Dictionary<string, Dictionary<ISymbol, HashSet<string>>> _indexes = [];
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

    /// <summary>Tests whether any compatible view of this interface has exactly one non-test implementation.</summary>
    public bool HasExactlyOne(CodeDeclaration declaration)
    {
        solution.CancellationToken.ThrowIfCancellationRequested();
        return _views.For(declaration.Source.Project).Any(view => CountInView(view, declaration) == 1);
    }

    private int CountInView(AnalysisProject[] projects, CodeDeclaration declaration)
    {
        var implementations = new HashSet<string>();
        foreach (var project in projects.Where(project => !project.Snapshot.IsTestProject))
        {
            if (!_views.Reaches(project, declaration.Source.Project.Snapshot.ContextId))
            {
                continue;
            }

            var assembly = ResolveAssembly(project, declaration.Source.Project);
            var target = assembly?.GetTypeByMetadataName(CodeType.MetadataNameOf(declaration.Symbol));
            if (target is null)
            {
                continue;
            }

            if (solution.Options.EnableOptimizations)
            {
                _indexLookups++;
                if (Index(project).TryGetValue(target, out var matches))
                {
                    implementations.UnionWith(matches);
                }

                continue;
            }

            foreach (var type in Definitions(project))
            {
                _definitionComparisons++;
                if (type.AllInterfaces.Any(contract => SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, target)))
                {
                    implementations.Add(project.Snapshot.ContextId + ":" + CodeType.MetadataNameOf(type));
                }
            }
        }

        return implementations.Count;
    }

    private Dictionary<ISymbol, HashSet<string>> Index(AnalysisProject project)
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

                if (implementations.Add(identity))
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

        return project.Compilation.SourceModule.ReferencedAssemblySymbols.SingleOrDefault(assembly =>
            assembly.Identity.Equals(owner.Compilation.Assembly.Identity));
    }

    private IReadOnlyList<INamedTypeSymbol> Definitions(AnalysisProject project)
    {
        if (!_definitions.TryGetValue(project.Snapshot.ContextId, out var definitions))
        {
            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            definitions = project.Sources.SelectMany(source => source.Tree.GetRoot(source.Project.CancellationToken).DescendantNodes()
                .OfType<TypeDeclarationSyntax>().Select(syntax => source.Model.GetDeclaredSymbol(syntax, source.Project.CancellationToken)))
                .OfType<INamedTypeSymbol>().Where(type => type.TypeKind is TypeKind.Class or TypeKind.Struct && !type.IsAbstract && seen.Add(type))
                .ToArray();
            _definitions.Add(project.Snapshot.ContextId, definitions);
        }

        return definitions;
    }

}
