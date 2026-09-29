namespace DrillPress.Operations;

/// <summary>Ordered string-building syntax with preserved reference provenance and interpolation formatting.</summary>
public sealed class BuiltString
{
    internal BuiltString(IReadOnlyList<BuiltStringPart> parts) => Parts = parts;

    /// <summary>The flattened source segments in evaluation order; no holes have been evaluated.</summary>
    public IReadOnlyList<BuiltStringPart> Parts { get; }

    /// <summary>Decoded source text segments, excluding holes and their possible runtime values.</summary>
    public IEnumerable<string> LiteralParts =>
        Parts.Where(part => part.Kind == BuiltStringPartKind.Literal).Select(part => part.Text!);

    /// <summary>Tests whether the first nonempty source segment is a hole satisfying the predicate.</summary>
    public bool StartsWith(Func<CodeExpression, bool> hole) =>
        Parts
            .FirstOrDefault(part => part.Kind == BuiltStringPartKind.Hole || part.Text?.Length > 0)
            ?.Value
            is { } value
        && hole(value);
}
