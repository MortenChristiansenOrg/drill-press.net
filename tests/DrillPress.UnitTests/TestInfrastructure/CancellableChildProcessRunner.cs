using DrillPress.Cli;

namespace DrillPress.UnitTests.TestInfrastructure;

/// <summary>Answers each child process from a script that can observe the run's cancellation.</summary>
internal sealed class CancellableChildProcessRunner(
    Func<string, IReadOnlyList<string>, CancellationToken, Task<ChildProcessResult>> execute
) : ChildProcessRunner
{
    public List<(string Executable, string[] Arguments)> Calls { get; } = [];

    public override Task<ChildProcessResult> CaptureAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        lock (Calls)
            Calls.Add((executable, arguments.ToArray()));
        return execute(executable, arguments, cancellationToken);
    }
}
