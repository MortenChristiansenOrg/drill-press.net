using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Shares lazy candidate collections across all rules in a loaded source-project graph.</summary>
public sealed class AnalysisSolution
{
    internal bool IsPlanningCoverage { get; init; }
    internal bool RequiresCertainCoverageMatches { get; init; }
    private AnalysisSolution? _oppositeCoveragePlanning;

    internal AnalysisSolution OppositeCoveragePlanning()
    {
        if (!IsPlanningCoverage)
            return this;
        lock (_facts)
        {
            if (_oppositeCoveragePlanning is null)
            {
                _oppositeCoveragePlanning = new(Projects, Options, CancellationToken, false)
                {
                    IsPlanningCoverage = true,
                    RequiresCertainCoverageMatches = !RequiresCertainCoverageMatches,
                };
                _oppositeCoveragePlanning._oppositeCoveragePlanning = this;
            }
            return _oppositeCoveragePlanning;
        }
    }

    private readonly Lazy<IReadOnlyList<MemberReference>> _references;
    private readonly Lazy<IReadOnlyList<CodeMethod>> _methods;
    private readonly Lazy<IReadOnlyList<CodeTypeDefinition>> _types;
    private long _memberBindings;
    private readonly Lazy<MemberCandidateIndex> _memberCandidates;
    private readonly Lazy<TypeReferenceIndex> _typeReferences;
    private readonly Dictionary<object, Lazy<object>> _facts = [];
    private readonly Lazy<ProjectGraph> _projectGraph;
    private readonly Lazy<IReadOnlySet<string>> _aliases;

    internal T Cached<T>(object key, Func<T> create)
        where T : notnull
    {
        Lazy<object> value;
        lock (_facts)
        {
            if (!_facts.TryGetValue(key, out value!))
            {
                value = new(() => create());
                _facts.Add(key, value);
            }
        }

        CancellationToken.ThrowIfCancellationRequested();
        return (T)value.Value;
    }

    /// <summary>Creates an analysis over separately evaluated project contexts.</summary>
    public AnalysisSolution(
        IReadOnlyList<AnalysisProject> projects,
        CancellationToken cancellationToken = default
    )
        : this(projects, new AnalysisOptions(), cancellationToken) { }

    /// <summary>Creates an analysis with explicit execution and profiling options.</summary>
    public AnalysisSolution(
        IReadOnlyList<AnalysisProject> projects,
        AnalysisOptions options,
        CancellationToken cancellationToken = default
    )
        : this(projects, options, cancellationToken, true) { }

    // Coverage planning analyses read the same projects without becoming their analysis.
    internal AnalysisSolution(
        IReadOnlyList<AnalysisProject> projects,
        AnalysisOptions options,
        CancellationToken cancellationToken,
        bool ownsProjects
    )
    {
        Options = options;
        CancellationToken = cancellationToken;
        Projects = projects.ToArray();
        if (ownsProjects)
            foreach (var project in Projects)
                project.BelongTo(this);
        Implementations = new(this);
        _projectGraph = new(() => new ProjectGraph(this));
        _memberCandidates = new(() => new MemberCandidateIndex(OrdinarySources, CancellationToken));
        _typeReferences = new(() =>
            new TypeReferenceIndex(
                Projects.SelectMany(project => project.Sources),
                CancellationToken
            )
        );
        _references = new(() =>
            Options.EnableOptimizations
                ? _memberCandidates.Value.Select(null).ToArray()
                : OrdinarySources.SelectMany(DiscoverReferences).ToArray()
        );
        _methods = new(() =>
            OrdinarySources
                .SelectMany(source =>
                    source
                        .Tree.GetRoot(source.Project.CancellationToken)
                        .DescendantNodes()
                        .OfType<MethodDeclarationSyntax>()
                        .Select(syntax => new CodeMethod(this, source, syntax))
                )
                .ToArray()
        );
        _types = new(DiscoverTypes);
        _aliases = new(() =>
            Projects
                .SelectMany(project => project.Sources)
                .SelectMany(source =>
                    source
                        .Tree.GetRoot(CancellationToken)
                        .DescendantNodes(node =>
                            node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax
                        )
                        .OfType<UsingDirectiveSyntax>()
                )
                .Select(directive => directive.Alias?.Name.Identifier.ValueText)
                .OfType<string>()
                .ToHashSet()
        );
    }

    /// <summary>Member references in analysis-target documents, binding only names and documents the filter can accept.</summary>
    internal IEnumerable<MemberReference> SelectMemberReferences(DiscoveryFilter filter) =>
        !Options.EnableOptimizations
            ? MemberReferences.Where(reference =>
                reference.Source.Project.Snapshot.IsAnalysisTarget
            )
            : _memberCandidates.Value.Select(
                filter.Names,
                source => source.Project.Snapshot.IsAnalysisTarget && filter.Includes(source)
            );

    /// <summary>Calls in these files, binding only accepted documents whose syntax can produce an accepted target name.</summary>
    internal IEnumerable<CodeInvocation> SelectInvocations(
        IReadOnlyList<CodeFile> files,
        DiscoveryFilter filter
    )
    {
        var sources = Accepted(files, filter)
            .Where(source =>
                filter.Names is not { } names
                || !Options.EnableOptimizations
                || names.AcceptsAny(source.Operations.InvocationNames)
            )
            .ToArray();
        PrepareOperations(sources);
        return sources.SelectMany(source => source.Operations.Invocations);
    }

    /// <summary>Every operation in the accepted files, in file and discovery order.</summary>
    internal IEnumerable<CodeOperation<IOperation>> SelectOperations(
        IReadOnlyList<CodeFile> files,
        DiscoveryFilter filter
    )
    {
        var sources = Accepted(files, filter).ToArray();
        PrepareOperations(sources);
        return sources.SelectMany(source =>
            source.Operations.All.Select(operation => new CodeOperation<IOperation>(
                source,
                operation
            ))
        );
    }

    /// <summary>Written object creations in the accepted files whose written type name can be accepted; target-typed and keyword creations always qualify.</summary>
    internal IEnumerable<CodeObjectCreation> SelectObjectCreations(
        IReadOnlyList<CodeFile> files,
        DiscoveryFilter filter
    ) =>
        Accepted(files, filter)
            .SelectMany(source => source.ObjectCreations)
            .Where(creation =>
                filter.Names is not { } names
                || !Options.EnableOptimizations
                || CreatedTypeName(creation.Syntax) is not { } name
                || names.Accepts(name)
                || Aliases.Contains(name)
            );

    private IEnumerable<AnalysisSource> Accepted(
        IReadOnlyList<CodeFile> files,
        DiscoveryFilter filter
    ) =>
        files
            .Select(file => file.Source)
            .Where(source => !Options.EnableOptimizations || filter.Includes(source));

    // The rightmost identifier of a written type, which is the created type's name unless it is an alias.
    private static string? CreatedTypeName(BaseObjectCreationExpressionSyntax creation) =>
        (creation as ObjectCreationExpressionSyntax)?.Type switch
        {
            SimpleNameSyntax name => name.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax qualified => qualified.Name.Identifier.ValueText,
            _ => null,
        };

    /// <summary>Every using alias declared in any loaded document, including global aliases in generated files.</summary>
    internal IReadOnlySet<string> Aliases => _aliases.Value;

    // Documents bind independently, so they bind in parallel; consumers still read them in order.
    private void PrepareOperations(IReadOnlyList<AnalysisSource> sources)
    {
        var pending = sources.Where(source => !source.Operations.IsBound).Distinct().ToArray();
        if (!Options.EnableOptimizations || pending.Length < 2)
            return;
        Parallel.ForEach(
            pending,
            new ParallelOptions { CancellationToken = CancellationToken },
            source => source.Operations.Prepare()
        );
    }

    /// <summary>Type references in analysis-target documents, binding only names and documents the filter can accept.</summary>
    internal IEnumerable<CodeTypeReference> SelectTypeReferences(DiscoveryFilter filter) =>
        _typeReferences.Value.Select(
            Options.EnableOptimizations ? filter.Names : null,
            source =>
                source.Project.Snapshot.IsAnalysisTarget
                && (!Options.EnableOptimizations || filter.Includes(source))
        );

    /// <summary>Stops evaluation and discovery within this analysis.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Execution strategy and measurements shared by all rules in this analysis.</summary>
    public AnalysisOptions Options { get; }

    /// <summary>All loaded contexts, including dependencies and alternate target frameworks.</summary>
    public IReadOnlyList<AnalysisProject> Projects { get; }

    /// <summary>Bound member expressions from ordinary source only.</summary>
    public IReadOnlyList<MemberReference> MemberReferences => _references.Value;

    /// <summary>Ordinary source methods, discovered on first use.</summary>
    public IReadOnlyList<CodeMethod> Methods => _methods.Value;

    /// <summary>Distinct ordinary source type definitions within each context.</summary>
    public IReadOnlyList<CodeTypeDefinition> Types => _types.Value;

    /// <summary>Cached source implementation analysis over compatible source graphs.</summary>
    public InterfaceImplementations Implementations { get; }

    /// <summary>Compatible evaluated project dependencies, shared by counterpart and ownership policies.</summary>
    public ProjectGraph ProjectGraph => _projectGraph.Value;

    /// <summary>Shared source inheritance and statically bound call-path analysis.</summary>
    public CodeRelationships Relationships => CodeRelationships.In(this);

    internal void WriteProfileCounters()
    {
        Options.Profile.Count(
            "member.symbol.bindings",
            _memberBindings
                + (_memberCandidates.IsValueCreated ? _memberCandidates.Value.Bindings : 0)
        );
        Options.Profile.Count(
            "type.symbol.bindings",
            _typeReferences.IsValueCreated ? _typeReferences.Value.Bindings : 0
        );
        Options.Profile.Count(
            "operation.bound.documents",
            Projects
                .SelectMany(project => project.Sources)
                .Count(source => source.Operations.IsBound)
        );
        Implementations.WriteProfileCounters();
    }

    private IEnumerable<AnalysisSource> OrdinarySources =>
        Projects
            .SelectMany(project => project.Sources)
            .Where(source => !source.Document.IsGenerated);

    private IReadOnlyList<CodeTypeDefinition> DiscoverTypes()
    {
        var result = new List<CodeTypeDefinition>();
        foreach (var project in Projects)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (
                var source in project
                    .Sources.Where(source => !source.Document.IsGenerated)
                    .OrderBy(source => source.Document.Path, StringComparer.Ordinal)
            )
            {
                foreach (
                    var syntax in source
                        .Tree.GetRoot(source.Project.CancellationToken)
                        .DescendantNodes()
                        .OfType<MemberDeclarationSyntax>()
                        .Where(node =>
                            node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax
                        )
                )
                {
                    if (
                        source.Model.GetDeclaredSymbol(syntax, source.Project.CancellationToken)
                            is INamedTypeSymbol symbol
                        && seen.Add(symbol)
                    )
                    {
                        result.Add(new CodeTypeDefinition(this, source, syntax, symbol));
                    }
                }
            }
        }

        return result;
    }

    private IEnumerable<MemberReference> DiscoverReferences(AnalysisSource source)
    {
        foreach (
            var name in source
                .Tree.GetRoot(source.Project.CancellationToken)
                .DescendantNodes()
                .OfType<SimpleNameSyntax>()
        )
        {
            source.Project.CancellationToken.ThrowIfCancellationRequested();
            ExpressionSyntax? expression =
                name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access
                : name is IdentifierNameSyntax
                && name.Parent is not (QualifiedNameSyntax or AliasQualifiedNameSyntax)
                    ? name
                : null;
            if (expression is null)
            {
                continue;
            }

            _memberBindings++;
            if (
                source.Model.GetSymbolInfo(expression, source.Project.CancellationToken).Symbol
                is (IFieldSymbol or IPropertySymbol or IMethodSymbol)
                    and { ContainingType: { IsAnonymousType: false } type } symbol
            )
            {
                yield return new MemberReference(source, expression, symbol);
            }
        }
    }
}
