namespace DrillPress.Manifest;

internal sealed record WorkerResponse(int ExitCode, string StandardOutput, string StandardError);
