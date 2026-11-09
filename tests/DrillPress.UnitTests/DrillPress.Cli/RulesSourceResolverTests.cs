using System.IO.Abstractions.TestingHelpers;
using System.Text;
using DrillPress.Cli;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Cli;

public sealed class RulesSourceResolverTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly CompilationSnapshot _snapshot = CompilationSnapshot.Create();
    private readonly string _work;
    private readonly string _project;
    private readonly string _bundle;
    private readonly TaskCompletionSource _exportStarted = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private Func<CancellationToken, Task<ChildProcessResult>> _build;
    private Func<CancellationToken, Task<ChildProcessResult>> _export;
    private bool _stopped;

    public RulesSourceResolverTests()
    {
        _work = _fileSystem.Path.GetFullPath("/work");
        _project = Work("rules", "Rules.csproj");
        _bundle = Work("rules", "bin", "Release", "net10.0", "Rules.dll");
        _fileSystem.AddFile(_project, "<Project />");
        _fileSystem.Directory.SetCurrentDirectory(_work);
        _build = _ =>
            Task.FromResult(
                new ChildProcessResult(
                    0,
                    _bundle + Environment.NewLine,
                    $"{Work("rules", "Program.cs")}(1,5): warning CS0168: Unused. [{_project}]\n"
                )
            );
        _export = _ => Task.FromResult(new ChildProcessResult(0, "", ""));
    }

    [Theory]
    [InlineData("rules/Rules.csproj")]
    [InlineData("rules")]
    public async Task A_rules_project_is_built_in_release_and_its_output_runs_like_a_compiled_bundle(
        string rules
    )
    {
        var runner = Runner();

        var result = await RunAsync(runner, "--rules", rules, "target.csproj");

        Assert.Equal((CliExitCode.Clean, "", ""), result);
        var snapshotPath = runner.Calls[1].Arguments[2];
        Assert.Equal(
            [
                [
                    "dotnet",
                    "build",
                    _project,
                    "--configuration",
                    "Release",
                    "-nologo",
                    "-t:Build",
                    "-getProperty:TargetPath",
                ],
                ["host", "export", "target.csproj", snapshotPath],
                [_bundle, "check", snapshotPath],
            ],
            runner.Calls.Select(call => (string[])[call.Executable, .. call.Arguments])
        );
    }

    [Fact]
    public async Task A_failed_rules_build_reports_only_its_errors_and_stops_the_target_export()
    {
        var program = Work("rules", "Program.cs");
        var error = $"{program}(1,28): error CS1002: ; expected [{_project}]";
        _build = async _ =>
        {
            await _exportStarted.Task;
            return new ChildProcessResult(
                1,
                _bundle + "\n",
                $"{program}(2,5): warning CS0168: Unused. [{_project}]\n{error}\n{error}\n"
            );
        };
        _export = Stoppable;
        var runner = Runner();

        var result = await RunAsync(runner, "--rules", "rules", "target.csproj");

        Assert.Equal(
            (
                CliExitCode.Failure,
                "",
                $"""
                drillpress: Rules project 'rules' failed to build:
                  {_fileSystem.Path.Combine("rules", "Program.cs")}(1,28): error CS1002: ; expected

                """.ReplaceLineEndings()
            ),
            result
        );
        Assert.True(_stopped);
    }

    [Fact]
    public async Task A_failed_target_export_stops_the_running_rules_build()
    {
        _build = Stoppable;
        _export = _ => Task.FromResult(new ChildProcessResult(3, "", ""));
        var runner = Runner();

        var result = await RunAsync(runner, "--rules", "rules", "target.csproj");

        Assert.Equal(
            (CliExitCode.Failure, "", $"drillpress: BuildHost exited 3.{Environment.NewLine}"),
            result
        );
        Assert.True(_stopped);
    }

    [Theory]
    [InlineData("missing.csproj", "Rules project 'missing.csproj' was not found.")]
    [InlineData("two", "Rules directory 'two' contains 2 .csproj files; specify one.")]
    [InlineData("none", "Rules directory 'none' contains 0 .csproj files; specify one.")]
    public async Task Missing_or_ambiguous_rules_projects_fail_before_any_tool_starts(
        string rules,
        string message
    )
    {
        _fileSystem.AddFile(Work("two", "A.csproj"), "<Project />");
        _fileSystem.AddFile(Work("two", "B.csproj"), "<Project />");
        _fileSystem.AddFile(Work("none", "Rules.dll"), "");
        var runner = Runner();

        var result = await RunAsync(runner, "--rules", rules, "target.csproj");

        Assert.Equal(
            (CliExitCode.Failure, "", $"drillpress: {message}{Environment.NewLine}"),
            result
        );
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task A_rules_project_without_a_single_output_asks_for_one_target_framework()
    {
        _build = _ => Task.FromResult(new ChildProcessResult(0, Environment.NewLine, ""));
        var runner = Runner();

        var result = await RunAsync(runner, "--rules", "rules/Rules.csproj", "target.csproj");

        Assert.Equal(
            (
                CliExitCode.Failure,
                "",
                $"drillpress: Rules project 'rules/Rules.csproj' has no single build output; set one TargetFramework.{Environment.NewLine}"
            ),
            result
        );
        Assert.Equal(["dotnet"], runner.Calls.Select(call => call.Executable));
    }

    [Fact]
    public async Task Streamed_compilation_starts_the_built_rules_process_before_the_export()
    {
        _export = _ => Task.FromResult(new ChildProcessResult(2, "", "host failed"));
        var runner = Runner();

        var result = await RunAsync(
            runner,
            "--rules",
            "rules",
            "--stream-compilation",
            "target.slnx"
        );

        Assert.Equal(
            (
                CliExitCode.Failure,
                "",
                $"drillpress: Generation pipeline failed: host failed{Environment.NewLine}"
            ),
            result
        );
        Assert.Equal(["dotnet", _bundle, "host"], runner.Calls.Select(call => call.Executable));
        Assert.True(_stopped);
    }

    private CancellableChildProcessRunner Runner() =>
        new(
            (executable, arguments, cancellationToken) =>
                executable switch
                {
                    "dotnet" => _build(cancellationToken),
                    "host" => ExportAsync(arguments, cancellationToken),
                    _ when arguments[0] == "check-generated" => GenerateAsync(
                        arguments,
                        cancellationToken
                    ),
                    _ => Task.FromResult(new ChildProcessResult(0, Response(), "")),
                }
        );

    private async Task<ChildProcessResult> ExportAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(
            arguments[2],
            _snapshot,
            cancellationToken
        );
        _exportStarted.SetResult();
        return await _export(cancellationToken);
    }

    private async Task<ChildProcessResult> GenerateAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        await new GeneratorTransport(_fileSystem, arguments[1]).WriteCapabilitiesAsync(
            [],
            cancellationToken
        );
        return await Stoppable(cancellationToken);
    }

    private async Task<ChildProcessResult> Stoppable(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new ChildProcessResult(0, "", "");
        }
        catch (OperationCanceledException)
        {
            _stopped = true;
            throw;
        }
    }

    private string Response() =>
        Encoding.UTF8.GetString(
            BundleResponseProtocol.Serialize(
                new BundleResponse(
                    BundleResponseProtocol.CurrentVersion,
                    _snapshot.RequestId,
                    [],
                    []
                )
            )
        );

    private async Task<(CliExitCode, string, string)> RunAsync(
        CancellableChildProcessRunner runner,
        params string[] arguments
    )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await new CliApplication(
            _fileSystem,
            runner,
            permissions: new StubSnapshotDirectoryPermissions(_fileSystem)
        ).RunAsync(
            ["check", "--build-host", "host", .. arguments],
            error,
            TestContext.Current.CancellationToken,
            output
        );
        return (exit, output.ToString(), error.ToString());
    }

    private string Work(params string[] segments) => _fileSystem.Path.Combine([_work, .. segments]);
}
