using System.IO.Abstractions;
using DrillPress.Cli;
using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class FailingGenerationProcessRunner(IFileSystem fileSystem) : ChildProcessRunner
{
    internal bool RulesStopped { get; private set; }

    public override async Task<ChildProcessResult> CaptureAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        if (arguments[0] != "check-generated")
            return new ChildProcessResult(2, "", "host failed");
        await new GeneratorTransport(fileSystem, arguments[1]).WriteCapabilitiesAsync(
            [],
            cancellationToken
        );
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new ChildProcessResult(0, "", "");
        }
        catch (OperationCanceledException)
        {
            RulesStopped = true;
            throw;
        }
    }
}
