namespace DrillPress;

/// <summary>The member or type names a condition can accept, letting query roots skip syntax whose names cannot match before binding it.</summary>
/// <remarks>A filter is either a finite set of exact names or a predicate such as a name glob. Roots must treat it as a necessary condition only; the condition itself still decides.</remarks>
internal sealed class NameFilter
{
    private readonly IReadOnlySet<string>? _names;
    private readonly Func<string, bool> _accepts;

    private NameFilter(IReadOnlySet<string>? names, Func<string, bool> accepts)
    {
        _names = names;
        _accepts = accepts;
    }

    internal static NameFilter Exactly(IEnumerable<string> names)
    {
        var set = names.ToHashSet();
        return new(set, set.Contains);
    }

    internal static NameFilter Matching(PathPattern pattern) => new(null, pattern.Matches);

    internal bool Accepts(string name) => _accepts(name);

    /// <summary>Whether any of the names written in some syntax can satisfy this filter.</summary>
    internal bool AcceptsAny(IReadOnlySet<string> names) =>
        _names is { } finite && finite.Count <= names.Count
            ? finite.Any(names.Contains)
            : names.Any(_accepts);

    internal NameFilter And(NameFilter other) =>
        _names is { } names ? Exactly(names.Where(other.Accepts))
        : other._names is { } otherNames ? Exactly(otherNames.Where(Accepts))
        : new(null, name => Accepts(name) && other.Accepts(name));

    internal NameFilter Or(NameFilter other) =>
        _names is { } names && other._names is { } otherNames
            ? Exactly(names.Union(otherNames))
            : new(null, name => Accepts(name) || other.Accepts(name));
}
