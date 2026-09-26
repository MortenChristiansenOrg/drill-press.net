namespace DrillPress.Release;

public sealed record ReleasePackage(
    string Id,
    string Version,
    string Path,
    IReadOnlyDictionary<string, string> Contents
);
