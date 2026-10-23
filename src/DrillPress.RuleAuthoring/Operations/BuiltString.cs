namespace DrillPress;

/// <summary>Ordered string-building syntax with preserved reference provenance and interpolation formatting.</summary>
public sealed class BuiltString
{
    internal BuiltString(IReadOnlyList<BuiltStringPart> parts) => Parts = parts;

    /// <summary>The flattened source segments in evaluation order; no holes have been evaluated.</summary>
    public IReadOnlyList<BuiltStringPart> Parts { get; }

    /// <summary>Combines adjacent literal segments while preserving hole expressions and formatting; a combined literal retains the first segment's source location.</summary>
    public BuiltString CoalesceLiterals()
    {
        var merged = new List<BuiltStringPart>();
        foreach (var part in Parts)
            if (
                part.Kind == BuiltStringPartKind.Literal
                && merged is [.., { Kind: BuiltStringPartKind.Literal } previous]
            )
                merged[^1] = previous with { Text = previous.Text + part.Text };
            else
                merged.Add(part);
        return new(merged.AsReadOnly());
    }

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
