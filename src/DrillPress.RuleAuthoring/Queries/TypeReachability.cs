using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>The bounded static discovery result for one seed, retaining completeness even when no reportable type was found.</summary>
public sealed class TypeReachability
{
    internal TypeReachability(
        TypeSeed seed,
        TypeTraversalStatus status,
        int visitedCount,
        IReadOnlyList<ReachedType> types
    )
    {
        Seed = seed;
        Status = status;
        VisitedCount = visitedCount;
        Types = types;
    }

    /// <summary>The original use-site evidence and compatible compilation context.</summary>
    public TypeSeed Seed { get; }

    /// <summary>Completeness relative to configured static edges, never runtime payload completeness.</summary>
    public TypeTraversalStatus Status { get; }

    /// <summary>Whether all configured reachable static edges were explored.</summary>
    public bool IsComplete => Status == TypeTraversalStatus.Complete;

    /// <summary>Constructed types visited before wrapper/output filtering.</summary>
    public int VisitedCount { get; }

    /// <summary>Selected emitted constructed types; definition deduplication happens only during declaration projection.</summary>
    public IReadOnlyList<ReachedType> Types { get; }

    /// <summary>Filters output without pruning graph traversal or erasing completeness evidence.</summary>
    public TypeReachability WhereType(Func<ITypeSymbol, bool> where) =>
        new(
            Seed,
            Status,
            VisitedCount,
            Array.AsReadOnly(Types.Where(node => where(node.Type)).ToArray())
        );

    internal static TypeReachability Traverse(TypeSeed seed, TypeTraversal policy)
    {
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Queue<(ITypeSymbol? Type, int Depth)>();
        var result = new List<ReachedType>();
        var status = TypeTraversalStatus.Complete;
        pending.Enqueue((seed.Type, 0));
        while (pending.TryDequeue(out var next))
        {
            seed.Source.Project.CancellationToken.ThrowIfCancellationRequested();
            if (next.Type is null || ContainsError(next.Type))
            {
                status |= TypeTraversalStatus.Unresolved;
                continue;
            }
            if (visited.Contains(next.Type))
                continue;
            if (next.Depth > policy.MaxDepth)
            {
                status |= TypeTraversalStatus.DepthLimit;
                continue;
            }
            if (visited.Count >= policy.MaxStates)
            {
                status |= TypeTraversalStatus.StateLimit;
                break;
            }
            visited.Add(next.Type);
            if (policy.Emit(next.Type))
                result.Add(new(next.Type, next.Depth, true));
            foreach (var edge in policy.Edges(next.Type))
            {
                seed.Source.Project.CancellationToken.ThrowIfCancellationRequested();
                pending.Enqueue((edge.Type, next.Depth + 1));
            }
        }
        return new(seed, status, visited.Count, result.AsReadOnly());
    }

    private static bool ContainsError(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Error
        || type is IArrayTypeSymbol array && ContainsError(array.ElementType)
        || type is INamedTypeSymbol named
            && (
                named.TypeArguments.Any(ContainsError)
                || named.ContainingType is { } containing && ContainsError(containing)
            );
}
