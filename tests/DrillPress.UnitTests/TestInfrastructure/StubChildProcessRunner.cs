using DrillPress.Cli;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class StubChildProcessRunner(
    Func<string, IReadOnlyList<string>, CancellationToken, Task<int>> execute
) : ChildProcessRunner
{
    public string StandardOutput { get; set; } = "";

    public override async Task<ChildProcessResult> CaptureAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    ) => new(await execute(executable, arguments, cancellationToken), StandardOutput, "");

    public override Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    ) => execute(executable, arguments, cancellationToken);
}
