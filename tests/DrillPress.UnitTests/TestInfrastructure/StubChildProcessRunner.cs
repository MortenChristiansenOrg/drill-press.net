using DrillPress.Cli;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class StubChildProcessRunner(
    Func<string, IReadOnlyList<string>, CancellationToken, Task<int>> execute) : ChildProcessRunner
{
    public override Task<int> RunAsync(
        string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        execute(executable, arguments, cancellationToken);
}
