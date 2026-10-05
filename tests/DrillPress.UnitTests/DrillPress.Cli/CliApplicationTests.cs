using System.IO.Abstractions.TestingHelpers;
using System.Text;
using DrillPress.Cli;
using DrillPress.Manifest;
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
    [InlineData("--help")]
    [InlineData("check", "--help")]
    [InlineData("fix", "--help")]
    public async Task Help_describes_the_primary_workflow_without_starting_tools(
        params string[] arguments
    )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var directories = _fileSystem.AllDirectories.ToArray();
        var runner = new StubChildProcessRunner(
            (_, _, _) => throw new InvalidOperationException("Unexpected process.")
        );
        var permissions = new StubSnapshotDirectoryPermissions(_fileSystem)
        {
            OnRestrict = _ => throw new InvalidOperationException("Unexpected snapshot directory."),
        };
        var application = new CliApplication(_fileSystem, runner, permissions: permissions);

        var result = await application.RunAsync(
            arguments,
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Clean, result);
        Assert.Equal("", error.ToString());
        Assert.Equal(
            """
            drillpress check|fix --rules <path> <target> [options]
            check reports findings; fix applies common-safe edits and reports the recheck.
            Targets: .sln, .slnx, .csproj, directory, .cs file, or quoted C# glob.
            --rules: compiled rule DLL or native executable. --build-host: override the packaged loader.
            --property Name=Value (repeatable)  Override MSBuild properties; restore SDK targets first.
            --include-referenced-projects  Also lint dependencies of a project target (default: selected project only).
            --validate-compilation  Reject compiler errors.  --profile  Write phase timings to stderr.
            --refresh-coverage  Rerun tests even when cached coverage inputs match.
            --show-fix-complexity  Include assigned agent fix effort once per rule.
            --fix-complexity <levels>  Select comma-separated trivial,local,complex,architectural,unspecified.
            Complexity selection applies to check, fix, and findings exit codes; omitted selects all rules.
            --no-optimization  Use exhaustive queries for comparison.  --help  Show this help.
            --version  Show the package and protocol versions.
            Exit codes: 0 clean, 1 findings, 2 failure. Fix failures may retain completed writes.

            """.ReplaceLineEndings("\n"),
            output.ToString()
        );
        Assert.Equal(directories, _fileSystem.AllDirectories);
        Assert.Empty(_fileSystem.AllFiles);
    }

    [Fact]
    public async Task Version_identifies_the_package_and_wire_contracts_without_starting_tools()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new CliApplication(
            _fileSystem,
            new StubChildProcessRunner(
                (_, _, _) => throw new InvalidOperationException("Unexpected process.")
            )
        );

        var result = await application.RunAsync(
            ["--version"],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Clean, result);
        Assert.Equal(
            $"drillpress {ComponentVersion.Current} (snapshot 5, response 4)\n",
            output.ToString()
        );
        Assert.Equal("", error.ToString());
        Assert.Empty(_fileSystem.AllFiles);
    }

    [Fact]
    public async Task Packaged_loader_is_resolved_from_the_application_directory()
    {
        var applicationDirectory = _fileSystem.Path.GetFullPath("installed-tool");
        var host = _fileSystem.Path.Combine(
            applicationDirectory,
            "buildhost",
            "DrillPress.BuildHost.dll"
        );
        _fileSystem.AddFile(host, new MockFileData("host"));
        var snapshot = CompilationSnapshot.Create();
        var calls = new List<string>();
        var runner = new StubChildProcessRunner(
            async (executable, arguments, _) =>
            {
                calls.Add(executable);
                await new CompilationSnapshotFile(_fileSystem).WriteAsync(
                    arguments.Last(),
                    snapshot
                );
                return 0;
            }
        )
        {
            StandardOutput = Encoding.UTF8.GetString(
                BundleResponseProtocol.Serialize(
                    new BundleResponse(
                        BundleResponseProtocol.CurrentVersion,
                        snapshot.RequestId,
                        [],
                        []
                    )
                )
            ),
        };
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new CliApplication(
            _fileSystem,
            runner,
            permissions: new StubSnapshotDirectoryPermissions(_fileSystem),
            applicationDirectory: applicationDirectory
        );

        var result = await application.RunAsync(
            ["check", "--rules", "rules", "target.csproj"],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Clean, result);
        Assert.Equal([host, "rules"], calls);
        Assert.Equal("", error.ToString());
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public async Task Missing_packaged_loader_reports_how_to_repair_installation()
    {
        var applicationDirectory = _fileSystem.Path.GetFullPath("missing-tool");
        var host = _fileSystem.Path.Combine(
            applicationDirectory,
            "buildhost",
            "DrillPress.BuildHost.dll"
        );
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new CliApplication(
            _fileSystem,
            new StubChildProcessRunner(
                (_, _, _) => throw new InvalidOperationException("Unexpected process.")
            ),
            applicationDirectory: applicationDirectory
        );

        var result = await application.RunAsync(
            ["check", "--rules", "rules", "target.csproj"],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal(
            $"drillpress: Packaged BuildHost was not found at '{host}'. Reinstall DrillPress.Cli {ComponentVersion.Current}, or use --build-host with a matching source build.{Environment.NewLine}",
            error.ToString()
        );
        Assert.Equal("", output.ToString());
        Assert.Empty(_fileSystem.AllFiles);
    }

    [Fact]
    public async Task Permission_failure_removes_the_directory_without_starting_tools()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var directory = "";
        var calls = new List<string>();
        var runner = new StubChildProcessRunner(
            (executable, _, _) =>
            {
                calls.Add(executable);
                return Task.FromResult(2);
            }
        );
        var permissions = new StubSnapshotDirectoryPermissions(_fileSystem)
        {
            OnRestrict = path =>
            {
                directory = path;
                throw new IOException("Cannot restrict snapshot access.");
            },
        };
        var application = new CliApplication(_fileSystem, runner, permissions: permissions);

        var result = await application.RunAsync(
            ["check", "--build-host", "host", "--rules", "rules", "target.csproj"],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal("", output.ToString());
        Assert.Equal(
            $"drillpress: Cannot restrict snapshot access.{Environment.NewLine}",
            error.ToString()
        );
        Assert.Empty(calls);
        Assert.False(_fileSystem.Directory.Exists(directory));
    }

    [Theory]
    [InlineData()]
    [InlineData("check")]
    [InlineData("check", "--build-host", "host", "--rules", "rules")]
    [InlineData(
        "check",
        "--build-host",
        "host",
        "--build-host",
        "other",
        "--rules",
        "rules",
        "target"
    )]
    public async Task Run_returns_failure_and_usage_for_invalid_arguments(params string[] arguments)
    {
        var error = new StringWriter();

        var exitCode = await new CliApplication(
            _fileSystem,
            new StubChildProcessRunner(
                (_, _, _) => throw new InvalidOperationException("Unexpected process.")
            )
        ).RunAsync(arguments, error, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(
            $"Usage: drillpress check|fix --rules <path> <target> [--build-host <path>] [--property Name=Value] [--validate-compilation] [--include-referenced-projects] [--profile] [--no-optimization] [--refresh-coverage] [--show-fix-complexity] [--fix-complexity <levels>]{Environment.NewLine}",
            error.ToString()
        );
    }

    [Theory]
    [InlineData(0, CliExitCode.Clean)]
    [InlineData(1, CliExitCode.Failure)]
    [InlineData(2, CliExitCode.Failure)]
    [InlineData(42, CliExitCode.Failure)]
    public async Task Shares_the_snapshot_with_both_tools_and_removes_it(
        int ruleResult,
        CliExitCode expected
    )
    {
        var snapshot = CompilationSnapshot.Create();
        var calls = new List<(string Executable, string[] Arguments)>();
        var observedSnapshot = "";
        var handlers = new Dictionary<string, Func<IReadOnlyList<string>, Task<int>>>
        {
            ["host"] = Export,
            ["rules"] = Check,
        };
        var runner = new StubChildProcessRunner(
            (executable, arguments, _) =>
            {
                calls.Add((executable, arguments.ToArray()));
                return handlers[executable](arguments);
            }
        )
        {
            StandardOutput = Encoding.UTF8.GetString(
                BundleResponseProtocol.Serialize(
                    new BundleResponse(
                        BundleResponseProtocol.CurrentVersion,
                        snapshot.RequestId,
                        [],
                        []
                    )
                )
            ),
        };
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
        var application = new CliApplication(
            _fileSystem,
            runner,
            permissions: new StubSnapshotDirectoryPermissions(_fileSystem)
        );

        var result = await application.RunAsync(
            ["check", "--build-host", "host", "--rules", "rules", "target.csproj"],
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

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
        var runner = new StubChildProcessRunner(
            (executable, arguments, _) =>
            {
                calls.Add(executable);
                snapshotPath = arguments[2];
                _fileSystem.File.WriteAllText(snapshotPath, "partial");
                return Task.FromResult(17);
            }
        );
        var application = new CliApplication(
            _fileSystem,
            runner,
            permissions: new StubSnapshotDirectoryPermissions(_fileSystem)
        );

        var result = await application.RunAsync(
            ["check", "--build-host", "host", "--rules", "rules", "target.csproj"],
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal(["host"], calls);
        Assert.False(_fileSystem.Directory.Exists(_fileSystem.Path.GetDirectoryName(snapshotPath)));
    }

    [Fact]
    public async Task Cancellation_propagates_after_temporary_files_are_removed()
    {
        var snapshotPath = "";
        var runner = new StubChildProcessRunner(
            (_, arguments, token) =>
            {
                snapshotPath = arguments[2];
                _fileSystem.File.WriteAllText(snapshotPath, "partial");
                throw new OperationCanceledException(token);
            }
        );
        var application = new CliApplication(
            _fileSystem,
            runner,
            permissions: new StubSnapshotDirectoryPermissions(_fileSystem)
        );

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            application.RunAsync(
                ["check", "--build-host", "host", "--rules", "rules", "target.csproj"],
                TextWriter.Null,
                TestContext.Current.CancellationToken
            )
        );

        Assert.False(_fileSystem.Directory.Exists(_fileSystem.Path.GetDirectoryName(snapshotPath)));
    }

    [Fact]
    public async Task Process_errors_are_reported_after_temporary_files_are_removed()
    {
        var snapshotPath = "";
        var runner = new StubChildProcessRunner(
            (_, arguments, _) =>
            {
                snapshotPath = arguments[2];
                _fileSystem.File.WriteAllText(snapshotPath, "partial");
                throw new IOException("Cannot launch tool.");
            }
        );
        var error = new StringWriter();
        var application = new CliApplication(
            _fileSystem,
            runner,
            permissions: new StubSnapshotDirectoryPermissions(_fileSystem)
        );

        var result = await application.RunAsync(
            ["check", "--build-host", "host", "--rules", "rules", "target.csproj"],
            error,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal($"drillpress: Cannot launch tool.{Environment.NewLine}", error.ToString());
        Assert.False(_fileSystem.Directory.Exists(_fileSystem.Path.GetDirectoryName(snapshotPath)));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(0, "loader logging")]
    [InlineData(
        0,
        "{\"protocolVersion\":99,\"requestId\":\"request\",\"contexts\":[],\"batches\":[]}"
    )]
    [InlineData(
        0,
        "{\"protocolVersion\":1,\"requestId\":\"foreign\",\"contexts\":[],\"batches\":[]}"
    )]
    [InlineData(
        1,
        "{\"protocolVersion\":1,\"requestId\":\"request\",\"contexts\":[],\"batches\":[]}"
    )]
    public async Task Invalid_bundle_output_never_reaches_public_stdout(
        int exitCode,
        string response
    )
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
        var runner = new StubChildProcessRunner(
            (executable, arguments, _) => handlers[executable](arguments)
        )
        {
            StandardOutput = response,
        };
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var result = await new CliApplication(
            _fileSystem,
            runner,
            permissions: new StubSnapshotDirectoryPermissions(_fileSystem)
        ).RunAsync(
            ["check", "--build-host", "host", "--rules", "rules", "target.csproj"],
            stderr,
            TestContext.Current.CancellationToken,
            stdout
        );

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal("", stdout.ToString());
        Assert.NotEmpty(stderr.ToString());
        Assert.False(_fileSystem.Directory.Exists(_fileSystem.Path.GetDirectoryName(snapshotPath)));
    }

    [Theory]
    [InlineData("trivial", "R0 Replace alpha.\nA.cs\n  +1:3\n", CliExitCode.Findings)]
    [InlineData("complex", "R1 Replace alpha.\nB.cs\n  +1:3\n", CliExitCode.Findings)]
    [InlineData("unspecified", "R2 Replace alpha.\nC.cs\n  +1:3\n", CliExitCode.Findings)]
    [InlineData(
        "Trivial, COMPLEX,trivial",
        "R0 Replace alpha.\nA.cs\n  +1:3\nR1 Replace alpha.\nB.cs\n  +1:3\n",
        CliExitCode.Findings
    )]
    [InlineData("local,architectural", "", CliExitCode.Clean)]
    public async Task Complexity_selection_filters_reporting_and_exit_codes_without_requiring_annotations(
        string levels,
        string expected,
        CliExitCode expectedExitCode
    )
    {
        var fixture = new FixComplexityFixture();
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(
            fixture.Files.FileSystem,
            fixture.Runner,
            permissions: new StubSnapshotDirectoryPermissions(fixture.Files.FileSystem)
        );

        var result = await cli.RunAsync(
            [
                "check",
                "--build-host",
                "host",
                "--rules",
                "rules",
                "target.csproj",
                "--fix-complexity",
                levels,
            ],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(expectedExitCode, result);
        Assert.Equal(expected, output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Equal(["host", "rules"], fixture.Runner.Calls.Select(call => call.Executable));
        Assert.Equal(
            ["check", fixture.Runner.Calls[0].Arguments[2]],
            fixture.Runner.Calls[1].Arguments
        );
        Assert.Equal(
            fixture.Files.Documents.Select(document => document.Text),
            fixture.Files.Texts()
        );
        Assert.Empty(fixture.Files.TemporaryFiles());
    }

    [Fact]
    public async Task Complexity_is_opt_in_and_unclassified_headers_remain_unannotated()
    {
        var fixture = new FixComplexityFixture();
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(
            fixture.Files.FileSystem,
            fixture.Runner,
            permissions: new StubSnapshotDirectoryPermissions(fixture.Files.FileSystem)
        );

        var result = await cli.RunAsync(
            [
                "check",
                "--build-host",
                "host",
                "--rules",
                "rules",
                "target.csproj",
                "--show-fix-complexity",
            ],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Findings, result);
        Assert.Equal(
            """
            R0 [fix:trivial] Replace alpha.
            A.cs
              +1:3
            R1 [fix:complex] Replace alpha.
            B.cs
              +1:3
            R2 Replace alpha.
            C.cs
              +1:3

            """.ReplaceLineEndings("\n"),
            output.ToString()
        );
        Assert.Equal("", error.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    [InlineData("0")]
    [InlineData("Trivial,99")]
    [InlineData("trivial,")]
    [InlineData(",local")]
    [InlineData("trivial,,complex")]
    [InlineData()]
    [InlineData("trivial", "--fix-complexity", "complex")]
    public async Task Invalid_complexity_filters_fail_clearly_before_starting_tools(
        params string[] levels
    )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var runner = new StubChildProcessRunner(
            (_, _, _) => throw new InvalidOperationException("Unexpected process.")
        );
        var cli = new CliApplication(_fileSystem, runner);

        var result = await cli.RunAsync(
            ["check", "--rules", "rules", "target.csproj", "--fix-complexity", .. levels],
            error,
            TestContext.Current.CancellationToken,
            output
        );

        Assert.Equal(CliExitCode.Failure, result);
        Assert.Equal("", output.ToString());
        Assert.Equal(
            """
            drillpress: --fix-complexity requires a comma-separated list of trivial, local, complex, architectural, or unspecified; specify the option once.
            Usage: drillpress check|fix --rules <path> <target> [--build-host <path>] [--property Name=Value] [--validate-compilation] [--include-referenced-projects] [--profile] [--no-optimization] [--refresh-coverage] [--show-fix-complexity] [--fix-complexity <levels>]

            """.ReplaceLineEndings(Environment.NewLine),
            error.ToString()
        );
        Assert.Empty(_fileSystem.AllFiles);
    }
}
