namespace DrillPress.Baselines;

/// <summary>Comparison against an explicitly supplied accepted .NET source analysis.</summary>
public enum SourceChange
{
    /// <summary>The same source membership and text existed previously.</summary>
    Unchanged,

    /// <summary>No matching source membership existed previously.</summary>
    Added,

    /// <summary>The source membership existed with different text.</summary>
    Modified,
}
