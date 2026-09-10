using DrillPress.Operations;
using DrillPress.Semantics;
using Microsoft.CodeAnalysis;

namespace DrillPress.Relationships;

/// <summary>Solution-scoped source relationships. Call paths follow statically bound targets, not possible runtime implementations, reflection or dependency injection.</summary>
public sealed class CodeRelationships
{
    private readonly AnalysisSolution _solution;
    private readonly Lazy<Dictionary<DeclarationKey, List<CodeInvocation>>> _calls;
    private readonly CompilationViews _views;
    private static readonly object _cacheKey = new();
    private static readonly CodeQuery<CodeInvocation> _semanticCalls = OperationQueries.InvocationsIn(Sources.FilesIncludingGenerated);

    private CodeRelationships(AnalysisSolution solution)
    {
        _solution = solution;
        _views = new(solution);
        _calls = new(BuildCalls);
    }

    private Dictionary<DeclarationKey, List<CodeInvocation>> BuildCalls()
    {
        var index = new Dictionary<DeclarationKey, List<CodeInvocation>>();
        foreach (var call in _semanticCalls.In(_solution))
        {
            var owner = call.Source.Model.GetEnclosingSymbol(call.Operation.Syntax.SpanStart, _solution.CancellationToken);
            if (owner is IMethodSymbol method && Key(method) is { } key)
            {
                if (!index.TryGetValue(key, out var calls))
                {
                    calls = [];
                    index.Add(key, calls);
                }

                calls.Add(call);
            }
        }

        return index;
    }

    /// <summary>Shares relationship analysis; the call-site index is built only when a call query first needs it.</summary>
    public static CodeRelationships In(AnalysisSolution solution) => solution.Cached(_cacheKey, () => new CodeRelationships(solution));

    /// <summary>Direct source calls in a method, excluding nested function bodies.</summary>
    public IReadOnlyList<CodeInvocation> CallsFrom(IMethodSymbol method) =>
        Key(method) is { } key && _calls.Value.TryGetValue(key, out var calls) ? calls.AsReadOnly() : [];

    /// <summary>Finds source calls bound to this declaration; metadata-only targets have no declaration key.</summary>
    public IReadOnlyList<CodeInvocation> CallersOf(IMethodSymbol method) => Key(method) is { } key
        ? _calls.Value.Values.SelectMany(calls => calls).Where(call => Key(call.Target) == key).ToArray() : [];

    /// <summary>Reports whether a bound source call path reaches the configured API. False does not prove absence of indirect runtime calls.</summary>
    public bool Reaches(IMethodSymbol method, CodeMember target)
    {
        var pending = new Stack<IMethodSymbol>();
        var seen = new HashSet<DeclarationKey>();
        pending.Push(method);
        while (pending.TryPop(out var current))
        {
            _solution.CancellationToken.ThrowIfCancellationRequested();
            if (Key(current) is not { } key || !seen.Add(key))
            {
                continue;
            }

            foreach (var call in CallsFrom(current))
            {
                if (target.Matches(call.Target))
                {
                    return true;
                }

                pending.Push(call.Target);
            }
        }

        return false;
    }

    /// <summary>Finds ordinary source types implementing an interface or inheriting a class in a compatible source view. Abstract and test types are retained for consumer filtering.</summary>
    public IReadOnlyList<CodeDeclaration> DerivedTypes(CodeDeclaration declaration)
    {
        var compatible = _views.For(declaration.Source.Project).SelectMany(view => view).ToHashSet();
        return _solution.Types.Where(candidate => compatible.Contains(candidate.Source.Project) &&
            Ancestors(candidate.Symbol).Any(symbol => SameDeclaration(symbol, declaration.Symbol))).ToArray();
    }

    /// <summary>Finds all ordinary partial declaration files within the type's owning context.</summary>
    public IReadOnlyList<AnalysisSource> FilesOf(CodeDeclaration declaration) => declaration.Source.Project.Sources
        .Where(source => !source.Document.IsGenerated && declaration.Symbol.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree == source.Tree)).ToArray();

    private static IEnumerable<INamedTypeSymbol> Ancestors(INamedTypeSymbol type)
    {
        foreach (var contract in type.AllInterfaces)
        {
            yield return contract;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static bool SameDeclaration(INamedTypeSymbol left, INamedTypeSymbol right) =>
        left.OriginalDefinition.DeclaringSyntaxReferences.Any(first => right.OriginalDefinition.DeclaringSyntaxReferences
            .Any(second => first.SyntaxTree == second.SyntaxTree && first.Span == second.Span));

    private static DeclarationKey? Key(IMethodSymbol method)
    {
        var reference = (method.ReducedFrom ?? method).OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault();
        return reference is null ? null : new(reference.SyntaxTree, reference.Span.Start);
    }

    private readonly record struct DeclarationKey(SyntaxTree Tree, int Start);
}
