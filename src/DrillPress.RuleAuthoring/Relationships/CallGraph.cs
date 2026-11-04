using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

// Resolves source functions and follows their calls lazily, once per analysis. Each function's sites are found the
// first time a query reaches it, so rules that never ask for call paths pay nothing.
internal sealed class CallGraph
{
    private static readonly object _key = new();
    private readonly AnalysisSolution _solution;
    private readonly Lazy<ILookup<SyntaxTree, AnalysisSource>> _sources;
    private readonly Lazy<Dictionary<(SyntaxTree Tree, int Start), CodeMethod>> _methods;
    private readonly ConcurrentDictionary<FunctionKey, CodeFunction> _functions = new();
    private readonly ConditionalWeakTable<
        CodeMember,
        ConcurrentDictionary<CallPaths, Dictionary<CodeFunction, bool>>
    > _reach = new();
    private readonly Lazy<CallTargets> _targets;
    private readonly ConcurrentDictionary<AnalysisSource, Lazy<DocumentCallSites>> _documents =
        new();

    // The first function whose sites a query needs binds every document, in parallel, as a solution-wide call query
    // would; other rules' queries share the bound operations.
    private readonly Lazy<bool> _prepared;

    private CallGraph(AnalysisSolution solution)
    {
        _solution = solution;
        _sources = new(() =>
            solution.Projects.SelectMany(project => project.Sources).ToLookup(source => source.Tree)
        );
        _methods = new(() =>
            solution.Methods.ToDictionary(method => (method.Source.Tree, method.Syntax.SpanStart))
        );
        _targets = new(() => new CallTargets(solution, this));
        _prepared = new(() =>
        {
            solution.PrepareOperations([
                .. solution.Projects.SelectMany(project => project.Sources),
            ]);
            return true;
        });
    }

    public static CallGraph In(AnalysisSolution solution) =>
        solution.Cached(_key, () => new CallGraph(solution));

    public AnalysisSolution Solution => _solution;

    public AnalysisSource? SourceOf(SyntaxTree tree, ISymbol symbol)
    {
        var sources = _sources.Value[tree];
        AnalysisSource? first = null;
        foreach (var source in sources)
        {
            first ??= source;
            if (
                SymbolEqualityComparer.Default.Equals(
                    source.Project.Compilation.Assembly,
                    symbol.ContainingAssembly
                )
            )
                return source;
        }

        return first;
    }

    public CodeMethod? MethodAt(CodeFunction function) =>
        _methods.Value.GetValueOrDefault((function.Source.Tree, function.Syntax.SpanStart));

    // Only a partial method needs its symbol, to resolve to its implementation part.
    public CodeFunction FunctionOf(CodeMethod method) =>
        method.HasExplicitModifier(Modifier.Partial)
        && method.Symbol?.PartialImplementationPart is { } implementation
        && Resolve(implementation) is { } function
            ? function
            : Function(method.Source, method.Syntax, FunctionKind.Method, false);

    // The source function a bound target runs; null for metadata members and lambdas.
    public CodeFunction? Resolve(IMethodSymbol symbol)
    {
        var definition = Normalize(symbol);
        if (definition.DeclaringSyntaxReferences.FirstOrDefault() is not { } reference)
            return ImplicitConstructor(definition);
        var syntax = reference.GetSyntax(_solution.CancellationToken);
        return
            KindOf(syntax, definition) is { } kind
            && SourceOf(reference.SyntaxTree, definition) is { } source
            ? Function(source, syntax, kind, false)
            : null;
    }

    public static IMethodSymbol Normalize(IMethodSymbol symbol)
    {
        var definition = (symbol.ReducedFrom ?? symbol).OriginalDefinition;
        return definition.PartialImplementationPart ?? definition;
    }

    private CodeFunction? ImplicitConstructor(IMethodSymbol definition)
    {
        if (
            definition
                is not {
                    MethodKind: MethodKind.Constructor,
                    IsImplicitlyDeclared: true,
                    IsStatic: false,
                    Parameters.IsEmpty: true
                }
            || definition.ContainingType.DeclaringSyntaxReferences.FirstOrDefault() is not { } owner
            || SourceOf(owner.SyntaxTree, definition) is not { } source
        )
            return null;
        return Function(
            source,
            owner.GetSyntax(_solution.CancellationToken),
            FunctionKind.Constructor,
            true
        );
    }

    private static FunctionKind? KindOf(SyntaxNode syntax, IMethodSymbol definition) =>
        syntax switch
        {
            MethodDeclarationSyntax => FunctionKind.Method,
            LocalFunctionStatementSyntax => FunctionKind.LocalFunction,
            ConstructorDeclarationSyntax when !definition.IsStatic => FunctionKind.Constructor,
            TypeDeclarationSyntax when definition.MethodKind == MethodKind.Constructor =>
                FunctionKind.Constructor,
            AccessorDeclarationSyntax or ArrowExpressionClauseSyntax => FunctionKind.Accessor,
            OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax =>
                FunctionKind.Operator,
            _ => null,
        };

    private CodeFunction Function(
        AnalysisSource source,
        SyntaxNode syntax,
        FunctionKind kind,
        bool isImplicit
    ) =>
        _functions.GetOrAdd(
            new(source.Tree, syntax.Span, isImplicit),
            _ => new CodeFunction(this, source, syntax, kind, isImplicit)
        );

    public IReadOnlyList<CallSite> Discover(CodeFunction function)
    {
        _ = _prepared.Value;
        return new CallSiteDiscovery(this, function).Discover();
    }

    public DocumentCallSites SitesIn(AnalysisSource source) =>
        _documents
            .GetOrAdd(
                source,
                _ => new(() => _solution.Shared("call-paths", () => new DocumentCallSites(source)))
            )
            .Value;

    // The steps a traversal may take from a function's own sites, in source order.
    public IEnumerable<CallEdge> Edges(CodeFunction function, CallPaths paths)
    {
        var project = function.Source.Project;
        foreach (var site in function.Sites)
        {
            if (site.Deferred && !paths.Delegates)
                continue;
            yield return new(site, site.Target, site.Callee(this), CallEdgeKind.Bound);
            if (paths.Dispatch && site.Dispatchable)
            {
                foreach (
                    var (target, callee) in _targets.Value.Implementations(site.Target, project)
                )
                    yield return new(site, target, callee, CallEdgeKind.Dispatch);
            }

            foreach (var configured in paths.Edges)
            {
                if (!Matches(configured.Call, site.Target))
                    continue;
                foreach (var (target, callee) in _targets.Value.Configured(configured, project))
                    yield return new(site, target, callee, CallEdgeKind.Configured);
            }
        }
    }

    // Names are compared first: matching a reduced extension call constructs its definition.
    public static bool Matches(CodeMember member, IMethodSymbol symbol) =>
        symbol.Name == member.Name && member.Matches(symbol)
        || symbol.AssociatedSymbol is { } owner
            && owner.Name == member.Name
            && member.Matches(owner);

    // Answers are cached per target, configuration and function: a completed search that finds nothing proves every
    // function it visited negative, and a found path proves each function on it positive.
    public bool Reaches(CodeFunction start, CodeMember target, CallPaths paths)
    {
        var known = _reach
            .GetValue(target, _ => new())
            .GetOrAdd(paths, _ => new Dictionary<CodeFunction, bool>());
        lock (known)
        {
            if (known.TryGetValue(start, out var answer))
                return answer;
            var parents = new Dictionary<CodeFunction, CodeFunction?> { [start] = null };
            var pending = new Stack<CodeFunction>();
            pending.Push(start);
            while (pending.TryPop(out var current))
            {
                foreach (var edge in Edges(current, paths))
                {
                    _solution.CancellationToken.ThrowIfCancellationRequested();
                    if (Matches(target, edge.Target))
                        return Found(known, parents, current);
                    if (
                        edge.Callee is not { } callee
                        || parents.ContainsKey(callee)
                        || paths.Boundary?.Invoke(callee) == true
                    )
                        continue;
                    if (known.TryGetValue(callee, out var reaches))
                    {
                        if (reaches)
                            return Found(known, parents, current);
                        continue;
                    }

                    parents.Add(callee, current);
                    pending.Push(callee);
                }
            }

            foreach (var visited in parents.Keys)
                known[visited] = false;
            return false;
        }
    }

    private static bool Found(
        Dictionary<CodeFunction, bool> known,
        Dictionary<CodeFunction, CodeFunction?> parents,
        CodeFunction? function
    )
    {
        for (; function is not null; function = parents[function])
            known[function] = true;
        return true;
    }

    public CallPath? PathTo(CodeFunction start, CodeMember target, CallPaths paths)
    {
        var parents = new Dictionary<CodeFunction, (CodeFunction Caller, CallEdge Edge)?>
        {
            [start] = null,
        };
        var pending = new Queue<CodeFunction>();
        pending.Enqueue(start);
        while (pending.TryDequeue(out var current))
        {
            foreach (var edge in Edges(current, paths))
            {
                _solution.CancellationToken.ThrowIfCancellationRequested();
                if (Matches(target, edge.Target))
                {
                    var steps = new List<CallStep> { Step(current, edge) };
                    for (var previous = parents[current]; previous is { } link; )
                    {
                        steps.Add(Step(link.Caller, link.Edge));
                        previous = parents[link.Caller];
                    }

                    steps.Reverse();
                    return new(steps.AsReadOnly());
                }

                if (
                    edge.Callee is { } callee
                    && !parents.ContainsKey(callee)
                    && paths.Boundary?.Invoke(callee) != true
                )
                {
                    parents.Add(callee, (current, edge));
                    pending.Enqueue(callee);
                }
            }
        }

        return null;
    }

    public IReadOnlyList<CodeFunction> Callees(
        CodeFunction start,
        bool sameType,
        bool transitive,
        CallPaths paths
    )
    {
        var owner = start.Symbol?.ContainingType?.OriginalDefinition;
        var found = new List<CodeFunction>();
        var seen = new HashSet<CodeFunction>();
        var pending = new Queue<CodeFunction>([start]);
        while (pending.TryDequeue(out var current))
        {
            foreach (var edge in Edges(current, paths))
            {
                _solution.CancellationToken.ThrowIfCancellationRequested();
                if (
                    edge.Callee is not { } callee
                    || sameType
                        && !SymbolEqualityComparer.Default.Equals(
                            callee.Symbol?.ContainingType?.OriginalDefinition,
                            owner
                        )
                    || !seen.Add(callee)
                )
                    continue;
                found.Add(callee);
                if (transitive && paths.Boundary?.Invoke(callee) != true)
                    pending.Enqueue(callee);
            }
        }

        return found.AsReadOnly();
    }

    public IReadOnlyList<CallStep> Steps(CodeFunction function, CallPaths paths) =>
        Edges(function, paths).Select(edge => Step(function, edge)).ToArray().AsReadOnly();

    private static CallStep Step(CodeFunction caller, CallEdge edge) =>
        new(
            caller,
            edge.Target,
            edge.Callee,
            edge.Kind,
            edge.Site.Deferred,
            edge.Site.Source,
            edge.Site.Source.Locate(edge.Site.Span)
        );

    private readonly record struct FunctionKey(SyntaxTree Tree, TextSpan Span, bool IsImplicit);
}
