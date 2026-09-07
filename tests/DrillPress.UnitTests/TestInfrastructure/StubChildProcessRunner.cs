using DrillPress.Cli;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class StubChildProcessRunner(
    Func<string, IReadOnlyList<string>, CancellationToken, Task<int>> execute) : IChildProcessRunner
{
    public Task<int> RunAsync(
        string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        execute(executable, arguments, cancellationToken);
}
