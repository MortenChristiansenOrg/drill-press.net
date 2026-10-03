using DrillPress.Engine;

namespace DrillPress.IntegrationTests.TestInfrastructure;

internal sealed class BlockingCoverageProcess(string testProcess, string readyPath)
    : CoverageProcess
{
    private readonly string _testProcess = testProcess;
    private readonly string _readyPath = readyPath;

    internal override Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string directory,
        CancellationToken cancellationToken
    ) =>
        base.RunAsync(
            "dotnet",
            [_testProcess, "export", _readyPath, "unused.snapshot"],
            directory,
            cancellationToken
        );
}
