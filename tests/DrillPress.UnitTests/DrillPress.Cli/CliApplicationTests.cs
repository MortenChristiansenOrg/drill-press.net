using System.IO.Abstractions.TestingHelpers;
using DrillPress.Cli;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Cli;

public sealed class CliApplicationTests
{
    [Fact]
    public void Public_construction_requires_no_external_dependencies()
    {
        var type = typeof(CliApplication);

        var constructors = type.GetConstructors();

        Assert.Equal([0], constructors.Select(constructor => constructor.GetParameters().Length));
    }

    private readonly MockFileSystem _fileSystem = new();

    [Theory]
    [InlineData()]
    [InlineData("check")]
    [InlineData("check", "--build-host", "host", "--rules", "rules")]
    [InlineData("check", "--build-host", "host", "--build-host", "other", "--rules", "rules", "target")]
    public async Task Run_returns_failure_and_usage_for_invalid_arguments(params string[] arguments)
    {
        var error = new StringWriter();

        var exitCode = await new CliApplication(_fileSystem,
            new StubChildProcessRunner((_, _, _) => throw new InvalidOperationException("Unexpected process."))).RunAsync(
            arguments,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(
            $"Usage: drillpress check --build-host <path> --rules <path> <project.csproj>{Environment.NewLine}",
            error.ToString());
    }

    [Theory]
    [InlineData(0, CliExitCode.Clean)]
    [InlineData(1, CliExitCode.Findings)]
    [InlineData(2, CliExitCode.Failure)]
    [InlineData(42, CliExitCode.Failure)]
    public async Task Shares_the_snapshot_with_both_tools_and_removes_it(int ruleResult, CliExitCode expected)
    {
        var calls = new List<(string Executable, string[] Arguments)>();
        var observedSnapshot = "";
        var handlers = new Dictionary<string, Func<IReadOnlyList<string>, Task<int>>>
        {
            ["host"] = Export,
            ["rules"] = Check,
        };
        var runner = new StubChildProcessRunner((executable, arguments, _) =>
        {
            calls.Add((executable, arguments.ToArray()));
            return handlers[executable](arguments);
        });
        Task<int> Export(IReadOnlyList<string> arguments)
        {
            _fileSystem.File.WriteAllText(arguments[2], "compiler inputs");
            return Task.FromResult(0);
        }
        Task<int> Check(IReadOnlyList<string> arguments)
        {
            observedSnapshot = _fileSystem.File.ReadAllText(arguments[1]);
            return Task.FromResult(ruleResult);
        }
        var application = new CliApplication(_fileSystem, runner);

        var result = await application.RunAsync(
            ["check", "--build-host", "host", "--rules", "rules", "target.csproj"],
            TextWriter.Null, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result);
        Assert.Equal("compiler inputs", observedSnapshot);
        Assert.Equal(["host", "rules"], calls.Select(call => call.Executable));
        var snapshotPath = calls[0].Arguments[2];
        Assert.Equal(["export", "target.csproj", snapshotPath], calls[0].Arguments);
        Assert.Equal(["check", snapshotPath], calls[1].Arguments);
        Assert.False(_fileSystem.Directory.Exists(_fileSystem.Path.GetDirectoryName(snapshotPath)));
    }

    [Fact]
    public async Task Export_failure_skips_rules_and_removes_temporary_files()
    {
        var calls = new List<string>();
        var snapshotPath = "";
        var runner = new StubChildProcessRunner((executable, arguments, _) =>
        {
            calls.Add(executable);
            snapshotPath = arguments[2];
            _fileSystem.File.WriteAllText(snapshotPath, "partial");
            return Task.FromResult(17);
        });
        var application = new CliApplication(_fileSystem, runner);

        var result = await application.RunAsync(
            ["check", "--build-host", "host", "--rules", "rules", "target.csproj"],
            TextWriter.Null, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal(["host"], calls);
        Assert.False(_fileSystem.Directory.Exists(_fileSystem.Path.GetDirectoryName(snapshotPath)));
    }

    [Fact]
    public async Task Cancellation_propagates_after_temporary_files_are_removed()
    {
        var snapshotPath = "";
        var runner = new StubChildProcessRunner((_, arguments, token) =>
        {
            snapshotPath = arguments[2];
            _fileSystem.File.WriteAllText(snapshotPath, "partial");
            throw new OperationCanceledException(token);
        });
        var application = new CliApplication(_fileSystem, runner);

        await Assert.ThrowsAsync<OperationCanceledException>(() => application.RunAsync(
            ["check", "--build-host", "host", "--rules", "rules", "target.csproj"],
            TextWriter.Null, TestContext.Current.CancellationToken));

        Assert.False(_fileSystem.Directory.Exists(_fileSystem.Path.GetDirectoryName(snapshotPath)));
    }

    [Fact]
    public async Task Process_errors_are_reported_after_temporary_files_are_removed()
    {
        var snapshotPath = "";
        var runner = new StubChildProcessRunner((_, arguments, _) =>
        {
            snapshotPath = arguments[2];
            _fileSystem.File.WriteAllText(snapshotPath, "partial");
            throw new IOException("Cannot launch tool.");
        });
        var error = new StringWriter();
        var application = new CliApplication(_fileSystem, runner);

        var result = await application.RunAsync(
            ["check", "--build-host", "host", "--rules", "rules", "target.csproj"],
            error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal($"drillpress: Cannot launch tool.{Environment.NewLine}", error.ToString());
        Assert.False(_fileSystem.Directory.Exists(_fileSystem.Path.GetDirectoryName(snapshotPath)));
    }
}
