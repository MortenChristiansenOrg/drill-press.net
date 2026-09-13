namespace DrillPress.BuildHost;

/// <summary>Controls SDK evaluation and optional compiler-error validation.</summary>
public sealed record SnapshotLoadOptions
{
    /// <summary>Global MSBuild properties; later command-line assignments replace earlier ones.</summary>
    public Dictionary<string, string> Properties { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Enumerates compiler errors after generators finish and rejects invalid compilations.</summary>
    public bool ValidateCompilation { get; init; }
}
