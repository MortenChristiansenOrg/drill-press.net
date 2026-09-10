using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Analysis;

/// <summary>Shares lazy candidate collections across all rules in a loaded source-project graph.</summary>
public sealed class AnalysisSolution
{
    private readonly Lazy<IReadOnlyList<MemberReference>> _references;
    private readonly Lazy<IReadOnlyList<CodeMethod>> _methods;
    private readonly Lazy<IReadOnlyList<CodeDeclaration>> _types;
    private long _memberBindings;
    private readonly Lazy<MemberCandidateIndex> _memberCandidates;
    private readonly bool _providedReferences;
    private readonly Dictionary<object, Lazy<object>> _facts = [];

    internal T Cached<T>(object key, Func<T> create) where T : notnull
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
    public AnalysisSolution(IReadOnlyList<AnalysisProject> projects, CancellationToken cancellationToken = default)
        : this(projects, new AnalysisOptions(), cancellationToken) { }

    /// <summary>Creates an analysis with explicit execution and profiling options.</summary>
    public AnalysisSolution(IReadOnlyList<AnalysisProject> projects, AnalysisOptions options, CancellationToken cancellationToken = default)
    {
        Options = options;
        CancellationToken = cancellationToken;
        Projects = projects.ToArray();
        Implementations = new(this);
        _memberCandidates = new(() => new MemberCandidateIndex(OrdinarySources, CancellationToken));
        _references = new(() => Options.EnableOptimizations
            ? _memberCandidates.Value.Select(null).ToArray()
            : OrdinarySources.SelectMany(DiscoverReferences).ToArray());
        _methods = new(() => OrdinarySources.SelectMany(source => source.Tree.GetRoot(source.Project.CancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Select(syntax => new CodeMethod(source, syntax))).ToArray());
        _types = new(DiscoverTypes);
    }

    internal AnalysisSolution(IReadOnlyList<MemberReference> references) : this(Array.Empty<AnalysisProject>())
    {
        _providedReferences = true;
        _references = new(() => references);
    }

    internal IEnumerable<MemberReference> SelectMemberReferences(IReadOnlySet<string>? names) =>
        _providedReferences || !Options.EnableOptimizations ? MemberReferences : _memberCandidates.Value.Select(names);

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
    public IReadOnlyList<CodeDeclaration> Types => _types.Value;

    /// <summary>Cached concrete implementation analysis over compatible source graphs.</summary>
    public InterfaceImplementations Implementations { get; }

    internal void WriteProfileCounters()
    {
        Options.Profile.Count("member.symbol.bindings", _memberBindings + (_memberCandidates.IsValueCreated ? _memberCandidates.Value.Bindings : 0));
        Implementations.WriteProfileCounters();
    }

    private IEnumerable<AnalysisSource> OrdinarySources => Projects.SelectMany(project => project.Sources)
        .Where(source => !source.Document.IsGenerated);

    private IReadOnlyList<CodeDeclaration> DiscoverTypes()
    {
        var result = new List<CodeDeclaration>();
        foreach (var project in Projects)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var source in project.Sources.Where(source => !source.Document.IsGenerated)
                .OrderBy(source => source.Document.Path, StringComparer.Ordinal))
            {
                foreach (var syntax in source.Tree.GetRoot(source.Project.CancellationToken).DescendantNodes().OfType<MemberDeclarationSyntax>()
                    .Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
                {
                    if (source.Model.GetDeclaredSymbol(syntax, source.Project.CancellationToken) is INamedTypeSymbol symbol && seen.Add(symbol))
                    {
                        result.Add(new CodeDeclaration(this, source, syntax, symbol));
                    }
                }
            }
        }

        return result;
    }

    private IEnumerable<MemberReference> DiscoverReferences(AnalysisSource source)
    {
        foreach (var name in source.Tree.GetRoot(source.Project.CancellationToken).DescendantNodes().OfType<SimpleNameSyntax>())
        {
            source.Project.CancellationToken.ThrowIfCancellationRequested();
            ExpressionSyntax? expression = name.Parent is MemberAccessExpressionSyntax access && access.Name == name
                ? access : name is IdentifierNameSyntax && name.Parent is not (QualifiedNameSyntax or AliasQualifiedNameSyntax)
                    ? name : null;
            if (expression is null)
            {
                continue;
            }

            _memberBindings++;
            if (source.Model.GetSymbolInfo(expression, source.Project.CancellationToken).Symbol is
                (IFieldSymbol or IPropertySymbol or IMethodSymbol) and { ContainingType: { IsAnonymousType: false } type } symbol)
            {
                yield return new MemberReference(CodeType.FromSymbol(type), symbol.Name, source.Locate(expression.Span))
                {
                    Source = source,
                    Syntax = expression,
                    Symbol = symbol,
                };
            }
        }
    }
}
