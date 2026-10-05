using DrillPress.Cli;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Cli;

public sealed class CliFixTests
{
    private static readonly string[] _arguments =
    [
        "fix",
        "--build-host",
        "host",
        "--rules",
        "rules",
        "target.csproj",
        "--property",
        "Configuration=Release",
        "--validate-compilation",
    ];

    [Fact]
    public async Task Cancelled_recheck_reports_retained_writes_and_propagates_after_cleanup()
    {
        var fixture = new FixFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var runner = new FixProcessRunner(fixture) { OnRecheck = cancellation.Cancel };
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(
            fixture.FileSystem,
            runner,
            fixture.Applier,
            new StubSnapshotDirectoryPermissions(fixture.FileSystem)
        );
        var paths = fixture
            .Paths.Select(path => System.Text.Json.JsonEncodedText.Encode(path).ToString())
            .ToArray();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            cli.RunAsync(_arguments, error, cancellation.Token, output)
        );

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal("", output.ToString());
        Assert.Equal(
            $"  changed: {paths[0]}{Environment.NewLine}  changed: {paths[1]}{Environment.NewLine}",
            error.ToString()
        );
        Assert.Equal(["😀é\r\nbeta\ngamma\r", "😀é\r\nbeta\ngamma\r"], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Writes_once_and_exports_the_same_target_options_again_before_rendering_remaining_findings()
    {
        var fixture = new FixFixture();
        var runner = new FixProcessRunner(fixture);
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(
            fixture.FileSystem,
            runner,
            fixture.Applier,
            new StubSnapshotDirectoryPermissions(fixture.FileSystem)
        );

        var result = await cli.RunAsync(
            _arguments,
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Clean, result);
        Assert.Equal("", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Equal(
            ["host", "rules", "host", "rules"],
            runner.Calls.Select(call => call.Executable)
        );
        Assert.Equal(runner.Calls[0].Arguments, runner.Calls[2].Arguments);
        Assert.Equal(
            [
                "export",
                "target.csproj",
                runner.Calls[0].Arguments[2],
                "--property",
                "Configuration=Release",
                "--validate-compilation",
            ],
            runner.Calls[0].Arguments
        );
        Assert.Equal(["😀é\r\nbeta\ngamma\r", "😀é\r\nbeta\ngamma\r"], fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Theory]
    [InlineData(true, false, "regeneration failed\n", "BuildHost exited 2.")]
    [InlineData(false, true, "recheck failed\n", "Rule bundle exited 2.")]
    public async Task Failed_verification_retains_writes_and_emits_no_stale_findings(
        bool export,
        bool check,
        string childError,
        string reason
    )
    {
        var fixture = new FixFixture();
        var runner = new FixProcessRunner(fixture)
        {
            FailRegeneration = export,
            FailRecheck = check,
        };
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(
            fixture.FileSystem,
            runner,
            fixture.Applier,
            new StubSnapshotDirectoryPermissions(fixture.FileSystem)
        );
        var paths = fixture
            .Paths.Select(path => System.Text.Json.JsonEncodedText.Encode(path).ToString())
            .ToArray();

        var result = await cli.RunAsync(
            _arguments,
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal("", output.ToString());
        Assert.Equal(
            $"{childError}drillpress: Files changed but verification failed: {reason}{Environment.NewLine}  changed: {paths[0]}{Environment.NewLine}  changed: {paths[1]}{Environment.NewLine}",
            error.ToString()
        );
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
        var cli = new CliApplication(
            fixture.FileSystem,
            runner,
            fixture.Applier,
            new StubSnapshotDirectoryPermissions(fixture.FileSystem)
        );

        var result = await cli.RunAsync(
            _arguments,
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Findings, result);
        Assert.Equal("", error.ToString());
        Assert.Equal(
            """
            DP1004 Replace alpha.
            A.cs
              1:3
            B.cs
              1:3

            """.ReplaceLineEndings("\n"),
            output.ToString().ReplaceLineEndings("\n")
        );
        Assert.Equal(["host", "rules"], runner.Calls.Select(call => call.Executable));
        Assert.Equal(fixture.Documents.Select(document => document.Text), fixture.Texts());
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Late_replacement_failure_reports_changed_and_failed_paths_without_rechecking()
    {
        var fixture = new FixFixture(fileCount: 3);
        fixture.Replacer.OnReplacement[fixture.Paths[1]] = () =>
            throw new IOException("replacement denied");
        var runner = new FixProcessRunner(fixture);
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(
            fixture.FileSystem,
            runner,
            fixture.Applier,
            new StubSnapshotDirectoryPermissions(fixture.FileSystem)
        );
        var paths = fixture
            .Paths.Select(path => System.Text.Json.JsonEncodedText.Encode(path).ToString())
            .ToArray();

        var result = await cli.RunAsync(
            _arguments,
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal("", output.ToString());
        Assert.Equal(
            $"drillpress: Fix stopped: replacement denied{Environment.NewLine}  changed: {paths[0]}{Environment.NewLine}  failed: {paths[1]}{Environment.NewLine}  pending: {paths[2]}{Environment.NewLine}",
            error.ToString()
        );
        Assert.Equal(["host", "rules"], runner.Calls.Select(call => call.Executable));
        Assert.Equal(
            ["😀é\r\nbeta\ngamma\r", fixture.Documents[1].Text, fixture.Documents[2].Text],
            fixture.Texts()
        );
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public async Task Complexity_selection_changes_only_selected_files_and_rechecks_with_the_same_filter()
    {
        var fixture = new FixComplexityFixture();
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(
            fixture.Files.FileSystem,
            fixture.Runner,
            fixture.Files.Applier,
            new StubSnapshotDirectoryPermissions(fixture.Files.FileSystem)
        );

        var result = await cli.RunAsync(
            [.. _arguments, "--fix-complexity", "trivial", "--show-fix-complexity"],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Clean, result);
        Assert.Equal("", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Equal(
            [
                "😀é\r\nbeta\ngamma\r",
                fixture.Files.Documents[1].Text,
                fixture.Files.Documents[2].Text,
            ],
            fixture.Files.Texts()
        );
        Assert.Equal(
            ["host", "rules", "host", "rules"],
            fixture.Runner.Calls.Select(call => call.Executable)
        );
        Assert.Empty(fixture.Files.TemporaryFiles());
    }

    [Fact]
    public async Task Complexity_selection_can_fix_unclassified_rules_and_preserve_assigned_rules()
    {
        var fixture = new FixComplexityFixture();
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(
            fixture.Files.FileSystem,
            fixture.Runner,
            fixture.Files.Applier,
            new StubSnapshotDirectoryPermissions(fixture.Files.FileSystem)
        );

        var result = await cli.RunAsync(
            [.. _arguments, "--fix-complexity", "unspecified"],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Clean, result);
        Assert.Equal("", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Equal(
            [
                fixture.Files.Documents[0].Text,
                fixture.Files.Documents[1].Text,
                "😀é\r\nbeta\ngamma\r",
            ],
            fixture.Files.Texts()
        );
        Assert.Empty(fixture.Files.TemporaryFiles());
    }

    [Fact]
    public async Task An_empty_complexity_selection_does_not_apply_or_recheck_any_fixes()
    {
        var fixture = new FixComplexityFixture();
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(
            fixture.Files.FileSystem,
            fixture.Runner,
            fixture.Files.Applier,
            new StubSnapshotDirectoryPermissions(fixture.Files.FileSystem)
        );

        var result = await cli.RunAsync(
            [.. _arguments, "--fix-complexity", "local"],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Clean, result);
        Assert.Equal("", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Equal(
            fixture.Files.Documents.Select(document => document.Text),
            fixture.Files.Texts()
        );
        Assert.Equal(["host", "rules"], fixture.Runner.Calls.Select(call => call.Executable));
        Assert.Empty(fixture.Files.TemporaryFiles());
    }

    [Fact]
    public async Task Complexity_selection_withholds_a_shared_atomic_batch_without_writing_any_files()
    {
        var fixture = new FixComplexityFixture();
        var runner = new FixProcessRunner(fixture.Files) { Response = fixture.SharedBatchResponse };
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(
            fixture.Files.FileSystem,
            runner,
            fixture.Files.Applier,
            new StubSnapshotDirectoryPermissions(fixture.Files.FileSystem)
        );

        var result = await cli.RunAsync(
            [.. _arguments, "--fix-complexity", "trivial", "--show-fix-complexity"],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Findings, result);
        Assert.Equal(
            """
            R0 [fix:trivial] Replace alpha.
            A.cs
              1:3

            """.ReplaceLineEndings("\n"),
            output.ToString()
        );
        Assert.Equal("", error.ToString());
        Assert.Equal(
            fixture.Files.Documents.Select(document => document.Text),
            fixture.Files.Texts()
        );
        Assert.Equal(["host", "rules"], runner.Calls.Select(call => call.Executable));
        Assert.Empty(fixture.Files.TemporaryFiles());
    }
}
