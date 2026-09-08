using DrillPress.Manifest;

namespace DrillPress.Cli;

internal enum CliCommand { Check, Fix }

internal sealed record RuleEvaluation(CompilationSnapshot Snapshot, ValidatedResult Result, string Response);
