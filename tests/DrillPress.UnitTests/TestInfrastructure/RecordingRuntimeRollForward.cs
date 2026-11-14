using DrillPress.BuildHost;

namespace DrillPress.UnitTests.TestInfrastructure;

/// <summary>Records restarts instead of starting BuildHost on another runtime.</summary>
internal sealed class RecordingRuntimeRollForward : RuntimeRollForward
{
    public bool RolledForward { get; init; }
    public BuildHostExitCode ExitCode { get; init; }
    public List<string[]> Restarts { get; } = [];

    public override bool IsRolledForward => RolledForward;

    public override Task<BuildHostExitCode> RestartAsync(
        string[] args,
        CancellationToken cancellationToken
    )
    {
        Restarts.Add(args);
        return Task.FromResult(ExitCode);
    }
}
