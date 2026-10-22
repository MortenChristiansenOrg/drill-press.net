namespace DrillPress;

/// <summary>What a query's conditions tell its root before candidates are bound: the member or type names and the documents they can accept.</summary>
/// <remarks>Both are necessary conditions only; the conditions themselves still decide. Roots ignore them when optimizations are disabled.</remarks>
internal sealed record DiscoveryFilter(NameFilter? Names, Func<AnalysisSource, bool>? Sources)
{
    internal static DiscoveryFilter None { get; } = new(null, null);

    internal bool Includes(AnalysisSource source) => Sources?.Invoke(source) ?? true;
}
