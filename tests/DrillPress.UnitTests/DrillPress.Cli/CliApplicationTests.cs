using System.IO.Abstractions.TestingHelpers;
using DrillPress.Cli;
using DrillPress.Manifest;
using System.Text;
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
            $"Usage: drillpress check|fix --build-host <path> --rules <path> <target> [--property Name=Value] [--validate-compilation] [--profile]{Environment.NewLine}",
            error.ToString());
    }

    [Theory]
    [InlineData(0, CliExitCode.Clean)]
    [InlineData(1, CliExitCode.Failure)]
    [InlineData(2, CliExitCode.Failure)]
    [InlineData(42, CliExitCode.Failure)]
    public async Task Shares_the_snapshot_with_both_tools_and_removes_it(int ruleResult, CliExitCode expected)
    {
        var snapshot = CompilationSnapshot.Create();
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
        }) { StandardOutput = Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(
            new BundleResponse(1, snapshot.RequestId, [], []))) };
        async Task<int> Export(IReadOnlyList<string> arguments)
        {
            await new CompilationSnapshotFile(_fileSystem).WriteAsync(arguments[2], snapshot);
            return 0;
        }
        Task<int> Check(IReadOnlyList<string> arguments)
        {
            observedSnapshot = arguments[1];
            return Task.FromResult(ruleResult);
        }
        var application = new CliApplication(_fileSystem, runner);

        var result = await application.RunAsync(
            ["check", "--build-host", "host", "--rules", "rules", "target.csproj"],
            TextWriter.Null, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result);
        Assert.Equal(calls[0].Arguments[2], observedSnapshot);
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
    [Theory]
    [InlineData(0, "")]
    [InlineData(0, "loader logging")]
    [InlineData(0, "{\"protocolVersion\":99,\"requestId\":\"request\",\"contexts\":[],\"batches\":[]}")]
    [InlineData(0, "{\"protocolVersion\":1,\"requestId\":\"foreign\",\"contexts\":[],\"batches\":[]}")]
    [InlineData(1, "{\"protocolVersion\":1,\"requestId\":\"request\",\"contexts\":[],\"batches\":[]}")]
    public async Task Invalid_bundle_output_never_reaches_public_stdout(int exitCode, string response)
    {
        var snapshot = CompilationSnapshot.Create() with { RequestId = "request" };
        var snapshotPath = "";
        async Task<int> Export(IReadOnlyList<string> arguments)
        {
            snapshotPath = arguments[2];
            await new CompilationSnapshotFile(_fileSystem).WriteAsync(snapshotPath, snapshot);
            return 0;
        }
        var handlers = new Dictionary<string, Func<IReadOnlyList<string>, Task<int>>>
        {
            ["host"] = Export,
            ["rules"] = _ => Task.FromResult(exitCode),
        };
        var runner = new StubChildProcessRunner((executable, arguments, _) => handlers[executable](arguments))
        {
            StandardOutput = response,
        };
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var result = await new CliApplication(_fileSystem, runner).RunAsync(
            ["check", "--build-host", "host", "--rules", "rules", "target.csproj"], stderr,
            TestContext.Current.CancellationToken, stdout);

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal("", stdout.ToString());
        Assert.NotEmpty(stderr.ToString());
        Assert.False(_fileSystem.Directory.Exists(_fileSystem.Path.GetDirectoryName(snapshotPath)));
    }

}
