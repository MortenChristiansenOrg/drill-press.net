using DrillPress.Cli;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Cli;

public sealed class CliFixTests
{
    private static readonly string[] _arguments = ["fix", "--build-host", "host", "--rules", "rules", "target.csproj", "--property", "Configuration=Release", "--validate-compilation"];

    [Fact]
    public async Task Writes_once_and_exports_the_same_target_options_again_before_rendering_remaining_findings()
    {
        var fixture = new FixFixture();
        var runner = new FixProcessRunner(fixture);
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(fixture.FileSystem, runner, fixture.Applier);

        var result = await cli.RunAsync(_arguments, error, TestContext.Current.CancellationToken, output);

        Assert.Equal(CliExitCode.Clean, result);
        Assert.Equal("", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Equal(["host", "rules", "host", "rules"], runner.Calls.Select(call => call.Executable));
        Assert.Equal(runner.Calls[0].Arguments, runner.Calls[2].Arguments);
        Assert.Equal(["export", "target.csproj", runner.Calls[0].Arguments[2], "--property", "Configuration=Release", "--validate-compilation"], runner.Calls[0].Arguments);
        Assert.Equal(["😀é\r\nbeta\ngamma\r", "😀é\r\nbeta\ngamma\r"], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Theory]
    [InlineData(true, false, "regeneration failed\n", "BuildHost exited 2.")]
    [InlineData(false, true, "recheck failed\n", "Rule bundle exited 2.")]
    public async Task Failed_verification_retains_writes_and_emits_no_stale_findings(bool export, bool check, string childError, string reason)
    {
        var fixture = new FixFixture();
        var runner = new FixProcessRunner(fixture) { FailRegeneration = export, FailRecheck = check };
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(fixture.FileSystem, runner, fixture.Applier);
        var paths = fixture.Paths.Select(path => System.Text.Json.JsonEncodedText.Encode(path).ToString()).ToArray();

        var result = await cli.RunAsync(_arguments, error, TestContext.Current.CancellationToken, output);

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal("", output.ToString());
        Assert.Equal($"{childError}drillpress: Files changed but verification failed: {reason}{Environment.NewLine}  changed: {paths[0]}{Environment.NewLine}  changed: {paths[1]}{Environment.NewLine}", error.ToString());
        Assert.Equal(["😀é\r\nbeta\ngamma\r", "😀é\r\nbeta\ngamma\r"], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Withheld_plan_reuses_original_analysis_without_rebuilding()
    {
        var fixture = new FixFixture();
        var runner = new FixProcessRunner(fixture) { WithholdFixes = true };
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(fixture.FileSystem, runner, fixture.Applier);

        var result = await cli.RunAsync(_arguments, error, TestContext.Current.CancellationToken, output);

        Assert.Equal(CliExitCode.Findings, result);
        Assert.Equal("", error.ToString());
        Assert.Equal("""
            DP1004 Replace alpha.
            A.cs
              1:3
            B.cs
              1:3

            """.ReplaceLineEndings("\n"), output.ToString().ReplaceLineEndings("\n"));
        Assert.Equal(["host", "rules"], runner.Calls.Select(call => call.Executable));
        Assert.Equal(fixture.Documents.Select(document => document.Text), fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Late_replacement_failure_reports_changed_and_failed_paths_without_rechecking()
    {
        var fixture = new FixFixture(fileCount: 3);
        fixture.Replacer.OnReplacement[fixture.Paths[1]] = () => throw new IOException("replacement denied");
        var runner = new FixProcessRunner(fixture);
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(fixture.FileSystem, runner, fixture.Applier);
        var paths = fixture.Paths.Select(path => System.Text.Json.JsonEncodedText.Encode(path).ToString()).ToArray();

        var result = await cli.RunAsync(_arguments, error, TestContext.Current.CancellationToken, output);

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal("", output.ToString());
        Assert.Equal($"drillpress: Fix stopped: replacement denied{Environment.NewLine}  changed: {paths[0]}{Environment.NewLine}  failed: {paths[1]}{Environment.NewLine}  pending: {paths[2]}{Environment.NewLine}", error.ToString());
        Assert.Equal(["host", "rules"], runner.Calls.Select(call => call.Executable));
        Assert.Equal(["😀é\r\nbeta\ngamma\r", fixture.Documents[1].Text, fixture.Documents[2].Text], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }
}
