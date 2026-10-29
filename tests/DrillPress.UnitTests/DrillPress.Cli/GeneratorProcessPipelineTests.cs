using System.IO.Abstractions.TestingHelpers;
using DrillPress.Cli;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Cli;

public sealed class GeneratorProcessPipelineTests
{
    [Fact]
    public async Task Fix_recheck_waits_for_new_capture_instead_of_reading_the_previous_snapshot()
    {
        var fixture = new FixFixture(fileCount: 1);
        var runner = new GeneratedFixProcessRunner(fixture);
        var application = new CliApplication(
            fixture.FileSystem,
            runner,
            fixes: fixture.Applier,
            permissions: new StubSnapshotDirectoryPermissions(fixture.FileSystem)
        );
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await application.RunAsync(
            [
                "fix",
                "--build-host",
                "host",
                "--rules",
                "rules",
                "--native-generators",
                "target.slnx",
            ],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Clean, result);
        Assert.Equal("", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Equal(["😀é\r\nbeta\ngamma\r"], fixture.Texts());
        Assert.Equal([false, false], runner.ExistingInputs);
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Capture_failure_stops_the_waiting_rule_process_and_reports_the_cause()
    {
        var files = new MockFileSystem();
        var runner = new FailingGenerationProcessRunner(files);
        var application = new CliApplication(
            files,
            runner,
            permissions: new StubSnapshotDirectoryPermissions(files)
        );
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await application.RunAsync(
            [
                "check",
                "--build-host",
                "host",
                "--rules",
                "rules",
                "--native-generators",
                "target.slnx",
            ],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal("", output.ToString());
        Assert.Equal(
            $"drillpress: Generation pipeline failed: host failed{Environment.NewLine}",
            error.ToString()
        );
        Assert.True(runner.RulesStopped);
    }
}
