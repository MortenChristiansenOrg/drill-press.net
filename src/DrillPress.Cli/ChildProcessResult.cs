namespace DrillPress.Cli;

/// <summary>Captured process output kept separate until its protocol is validated.</summary>
/// <param name="ExitCode">The child process outcome.</param>
/// <param name="StandardOutput">Internal protocol or captured loader logging.</param>
/// <param name="StandardError">Operational diagnostics.</param>
public sealed record ChildProcessResult(int ExitCode, string StandardOutput, string StandardError);
