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
