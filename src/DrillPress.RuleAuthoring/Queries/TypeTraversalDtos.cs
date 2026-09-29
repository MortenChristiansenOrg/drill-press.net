using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Why a finite traversal cannot establish absence/exact inventory. Complete is relative to the configured static graph.</summary>
[Flags]
public enum TypeTraversalStatus
{
    /// <summary>All configured reachable static edges were explored.</summary>
    Complete = 0,

    /// <summary>A seed or selected edge has unresolved compiler type evidence.</summary>
    Unresolved = 1,

    /// <summary>A previously unseen type exceeded the configured depth.</summary>
    DepthLimit = 2,

    /// <summary>The configured number of distinct constructed types was exhausted.</summary>
    StateLimit = 4,
}

/// <summary>A selected static graph edge, optionally retaining its property/wrapper/custom evidence.</summary>
/// <param name="Type">The destination type; null/error types mark incomplete discovery.</param>
/// <param name="Evidence">Consumer or compiler evidence for this edge.</param>
public sealed record TypeEdge(ITypeSymbol? Type, object? Evidence = null);

/// <summary>A reached constructed type, before deduplication to a reportable definition.</summary>
/// <param name="Type">The substituted type in the seed's compilation.</param>
/// <param name="Depth">The shortest selected static path length from the seed.</param>
/// <param name="Emit">Whether wrapper policy includes this node in returned models.</param>
public sealed record ReachedType(ITypeSymbol Type, int Depth, bool Emit);
