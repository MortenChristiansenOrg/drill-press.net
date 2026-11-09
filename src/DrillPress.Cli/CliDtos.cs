using DrillPress.Manifest;

namespace DrillPress.Cli;

internal enum CliCommand
{
    Check,
    Fix,
    Status,
}

internal sealed record RuleEvaluation(
    CompilationSnapshot Snapshot,
    ValidatedResult Result,
    string Response
);

internal sealed record GeneratorProcessResult(
    ChildProcessResult Export,
    ChildProcessResult Rules,
    string SnapshotPath
);

/// <summary>Repository defaults from a drillpress.json file, with its relative paths resolved.</summary>
/// <param name="DisplayPath">The file path relative to the current directory, used in messages.</param>
/// <param name="Rules">The rules source relative to the current directory, if set.</param>
/// <param name="Target">The analysis target relative to the current directory, if set.</param>
/// <param name="Properties">MSBuild global properties in file order.</param>
/// <param name="Select">Rule IDs to report, or null for every rule.</param>
/// <param name="Ignore">Rule IDs to leave out.</param>
/// <param name="Exclude">Full-path globs of source files that report nothing.</param>
internal sealed record RepositoryConfig(
    string DisplayPath,
    string? Rules,
    string? Target,
    KeyValuePair<string, string>[] Properties,
    string[]? Select,
    string[] Ignore,
    string[] Exclude
);
