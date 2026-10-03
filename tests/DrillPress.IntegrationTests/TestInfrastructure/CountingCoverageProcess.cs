using DrillPress.Engine;

namespace DrillPress.IntegrationTests.TestInfrastructure;

internal sealed class CountingCoverageProcess : CoverageProcess
{
    public int Collections { get; private set; }

    internal override Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string directory,
        CancellationToken cancellationToken
    )
    {
        if (arguments[0] == "collect")
            Collections++;
        return base.RunAsync(executable, arguments, directory, cancellationToken);
    }
}
