using System.IO.Abstractions.TestingHelpers;
using System.Text;
using DrillPress.Cli;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Cli;

public sealed class RepositoryConfigFileTests
{
    private const string Usage =
        "Usage: drillpress check|fix|status [--rules <path>] [<target>] [--config <path>] [--select <ids>] [--ignore <ids>] [--exclude <glob>] [--build-host <path>] [--baseline <revision|directory>] [--property Name=Value] [--validate-compilation] [--include-referenced-projects] [--profile] [--parallel-queries] [--stream-compilation] [--no-optimization] [--refresh-coverage] [--skip-coverage] [--explain-coverage] [--explain-fixes] [--show-fix-complexity] [--fix-complexity <levels>]";

    private const string Defaults = """
        {
          // Committed defaults for developers, agents and CI.
          "rules": "eng/Rules.dll",
          "target": "App.slnx",
          "properties": { "Configuration": "Release", "TargetFramework": "net10.0" },
          "select": ["DP1", "DP2"],
          "ignore": ["DP3"],
          "exclude": ["third_party/", "eng/**"],
        }
        """;

    private readonly MockFileSystem _fileSystem = new();
    private readonly string _root;
    private readonly List<string[]> _calls = [];

    public RepositoryConfigFileTests()
    {
        _root = _fileSystem.Path.GetFullPath("/repo");
        _fileSystem.Directory.CreateDirectory(_fileSystem.Path.Combine(_root, "src", "App"));
    }

    [Fact]
    public async Task Defaults_found_above_the_current_directory_supply_the_whole_invocation()
    {
        Configure("drillpress.json", Defaults, "src", "App");
        var error = new StringWriter();

        var result = await RunAsync(error, "check", "--build-host", "host");
        var snapshot = _calls[0][3];

        Assert.Equal((CliExitCode.Clean, ""), (result, error.ToString()));
        Assert.Equal(
            [
                [
                    "host",
                    "export",
                    _fileSystem.Path.Combine("..", "..", "App.slnx"),
                    snapshot,
                    "--property",
                    "Configuration=Release",
                    "--property",
                    "TargetFramework=net10.0",
                ],
                [
                    _fileSystem.Path.Combine("..", "..", "eng", "Rules.dll"),
                    "check",
                    snapshot,
                    "--select",
                    "DP1",
                    "--select",
                    "DP2",
                    "--ignore",
                    "DP3",
                    "--exclude",
                    _fileSystem.Path.Combine(_root, "third_party", "**"),
                    "--exclude",
                    _fileSystem.Path.Combine(_root, "eng", "**"),
                ],
            ],
            _calls
        );
    }

    [Fact]
    public async Task Command_line_values_replace_or_follow_the_defaults()
    {
        Configure("drillpress.json", Defaults, "src", "App");
        var error = new StringWriter();

        var result = await RunAsync(
            error,
            "fix",
            "--build-host",
            "host",
            "--rules",
            "other.dll",
            "Local.csproj",
            "--property",
            "Configuration=Debug",
            "--select",
            "DP9, DP8",
            "--ignore",
            "DP4",
            "--exclude",
            "gen/*.cs"
        );
        var snapshot = _calls[0][3];

        Assert.Equal((CliExitCode.Clean, ""), (result, error.ToString()));
        Assert.Equal(
            [
                [
                    "host",
                    "export",
                    "Local.csproj",
                    snapshot,
                    "--property",
                    "Configuration=Release",
                    "--property",
                    "TargetFramework=net10.0",
                    "--property",
                    "Configuration=Debug",
                ],
                [
                    "other.dll",
                    "check",
                    snapshot,
                    "--select",
                    "DP9",
                    "--select",
                    "DP8",
                    "--ignore",
                    "DP4",
                    "--exclude",
                    _fileSystem.Path.Combine(_root, "third_party", "**"),
                    "--exclude",
                    _fileSystem.Path.Combine(_root, "eng", "**"),
                    "--exclude",
                    _fileSystem.Path.Combine(_root, "src", "App", "gen", "*.cs"),
                ],
            ],
            _calls
        );
    }

    [Fact]
    public async Task An_explicit_file_resolves_its_paths_and_keeps_properties_from_loose_targets()
    {
        Configure(
            _fileSystem.Path.Combine("shared", "drillpress.json"),
            """
            {
              "rules": "Rules.dll",
              "properties": { "Configuration": "Release" },
              "exclude": ["*.g.cs"]
            }
            """,
            "src",
            "App"
        );
        var error = new StringWriter();

        var result = await RunAsync(
            error,
            "status",
            "--build-host",
            "host",
            "--config",
            _fileSystem.Path.Combine("..", "..", "shared", "drillpress.json"),
            "Program.cs"
        );
        var snapshot = _calls[0][3];

        Assert.Equal((CliExitCode.Clean, ""), (result, error.ToString()));
        Assert.Equal(
            [
                ["host", "export", "Program.cs", snapshot],
                [
                    _fileSystem.Path.Combine("..", "..", "shared", "Rules.dll"),
                    "check",
                    snapshot,
                    "--exclude",
                    _fileSystem.Path.Combine(_root, "shared", "*.g.cs"),
                ],
            ],
            _calls
        );
    }

    [Theory]
    [InlineData(
        """{ "rule": "Rules.dll" }""",
        "unknown key 'rule'; use rules, target, properties, select, ignore or exclude"
    )]
    [InlineData(
        """{ "rules": ["DP1"] }""",
        "'rules' is the rules project or bundle path; list rule IDs in 'select'"
    )]
    [InlineData("""{ "rules": " " }""", "'rules' must be a path")]
    [InlineData("""{ "target": 1 }""", "'target' must be a path")]
    [InlineData(
        """{ "target": "A.slnx", "target": "B.slnx" }""",
        "'target' appears more than once"
    )]
    [InlineData(
        """{ "properties": { "Configuration": true } }""",
        "'properties' must map MSBuild property names to string values"
    )]
    [InlineData("""{ "properties": { "A=B": "C" } }""", "'properties' has an invalid name 'A=B'")]
    [InlineData("""{ "select": [] }""", "'select' must be a non-empty array of rule IDs")]
    [InlineData("""{ "ignore": "DP1" }""", "'ignore' must be an array of rule IDs")]
    [InlineData("""{ "exclude": ["vendor/**", ""] }""", "'exclude' must be an array of path globs")]
    [InlineData("[]", "expected a JSON object")]
    [InlineData(
        """{ "bad\nkey": true }""",
        "unknown key 'bad\\nkey'; use rules, target, properties, select, ignore or exclude"
    )]
    [InlineData(
        """{ "target": "\uD800" }""",
        "invalid JSON: Cannot read incomplete UTF-16 JSON text as string with missing low surrogate"
    )]
    public async Task Invalid_defaults_fail_with_one_line_before_starting_tools(
        string content,
        string message
    )
    {
        Configure("drillpress.json", content);
        var error = new StringWriter();

        var result = await RunAsync(error, "check", "--build-host", "host");

        Assert.Equal(
            (CliExitCode.Failure, $"drillpress: drillpress.json: {message}.{Environment.NewLine}"),
            (result, error.ToString())
        );
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Malformed_json_reports_where_parsing_stopped()
    {
        Configure("drillpress.json", """{ "rules": "Rules.dll" """);
        var error = new StringWriter();

        var result = await RunAsync(error, "check");

        Assert.Equal(
            (
                CliExitCode.Failure,
                $"drillpress: drillpress.json: invalid JSON: Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. LineNumber: 0 | BytePositionInLine: 23.{Environment.NewLine}"
            ),
            (result, error.ToString())
        );
    }

    [Fact]
    public async Task A_missing_explicit_file_fails_before_starting_tools()
    {
        _fileSystem.Directory.SetCurrentDirectory(_root);
        var error = new StringWriter();

        var result = await RunAsync(error, "check", "--config", "missing.json");

        Assert.Equal(
            (
                CliExitCode.Failure,
                $"drillpress: Config file 'missing.json' was not found.{Environment.NewLine}"
            ),
            (result, error.ToString())
        );
        Assert.Empty(_calls);
    }

    [Theory]
    [InlineData("""{ "rules": "Rules.dll" }""", "Pass a target or set 'target'")]
    [InlineData("""{ "target": "App.slnx" }""", "Pass --rules <path> or set 'rules'")]
    public async Task Incomplete_defaults_name_the_missing_value(string content, string message)
    {
        Configure("drillpress.json", content);
        var error = new StringWriter();

        var result = await RunAsync(error, "check", "--build-host", "host");

        Assert.Equal(
            (
                CliExitCode.Failure,
                $"drillpress: {message} in drillpress.json.{Environment.NewLine}{Usage}{Environment.NewLine}"
            ),
            (result, error.ToString())
        );
        Assert.Empty(_calls);
    }

    [Theory]
    [InlineData("vendor/", "vendor", "**")]
    [InlineData("vendor\\", "vendor", "**")]
    [InlineData("**/Generated/*.cs", "**", "Generated", "*.cs")]
    public async Task Command_line_exclusions_resolve_against_the_current_directory(
        string pattern,
        params string[] expected
    )
    {
        _fileSystem.Directory.SetCurrentDirectory(_root);
        var error = new StringWriter();

        var result = await RunAsync(
            error,
            "check",
            "--build-host",
            "host",
            "--rules",
            "rules",
            "target.csproj",
            "--exclude",
            pattern
        );

        Assert.Equal((CliExitCode.Clean, ""), (result, error.ToString()));
        Assert.Equal(
            [
                "rules",
                "check",
                _calls[0][3],
                "--exclude",
                _fileSystem.Path.Combine([_root, .. expected]),
            ],
            _calls[1]
        );
    }

    // Writes the file under the root and runs from the given directory below the root.
    private void Configure(string path, string content, params string[] currentDirectory)
    {
        _fileSystem.AddFile(_fileSystem.Path.Combine(_root, path), new MockFileData(content));
        _fileSystem.Directory.SetCurrentDirectory(
            _fileSystem.Path.Combine([_root, .. currentDirectory])
        );
    }

    private Task<CliExitCode> RunAsync(TextWriter error, params string[] arguments)
    {
        var snapshot = CompilationSnapshot.Create();
        var runner = new StubChildProcessRunner(
            async (executable, arguments, cancellationToken) =>
            {
                _calls.Add([executable, .. arguments]);
                if (executable == "host")
                    await new CompilationSnapshotFile(_fileSystem).WriteAsync(
                        arguments[2],
                        snapshot,
                        cancellationToken
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
        return new CliApplication(
            _fileSystem,
            runner,
            permissions: new StubSnapshotDirectoryPermissions(_fileSystem)
        ).RunAsync(arguments, error, TestContext.Current.CancellationToken, TextWriter.Null);
    }
}
