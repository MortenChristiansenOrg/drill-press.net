using DrillPress.Cli;
using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class GeneratedFixProcessRunner(FixFixture fixture) : ChildProcessRunner
{
    private readonly FixProcessRunner _ordinary = new(fixture);

    internal List<bool> ExistingInputs { get; } = [];

    public override async Task<ChildProcessResult> CaptureAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        if (arguments[0] != "check-generated")
            return await _ordinary.CaptureAsync(executable, arguments, cancellationToken);
        var input = arguments[2];
        ExistingInputs.Add(fixture.FileSystem.File.Exists(input));
        await new GeneratorTransport(fixture.FileSystem, arguments[1]).WriteCapabilitiesAsync(
            [],
            cancellationToken
        );
        while (!fixture.FileSystem.File.Exists(input))
            await Task.Delay(1, cancellationToken);
        var snapshots = new CompilationSnapshotFile(fixture.FileSystem);
        var snapshot = await snapshots.ReadAsync(input, cancellationToken);
        await snapshots.WriteAsync(arguments[3], snapshot, cancellationToken);
        return await _ordinary.CaptureAsync("rules", ["check", input], cancellationToken);
    }
}
