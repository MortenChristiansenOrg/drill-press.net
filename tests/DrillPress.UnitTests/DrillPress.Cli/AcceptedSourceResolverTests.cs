using System.IO.Abstractions.TestingHelpers;
using System.Text;
using DrillPress.Cli;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Cli;

public sealed class AcceptedSourceResolverTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly ContractFixture _fixture = new();
    private readonly List<string[]> _git = [];
    private readonly string _work;
    private string? _acceptedRoot;
    private string? _batchInput;
    private string? _topLevel;
    private string _prefix = "";
    private readonly Dictionary<string, string> _otherTopLevels = [];
    private (string Metadata, string Path, string Text)[] _tree = [];
    private (string Path, string Text, bool Removed)[] _acceptedFiles = [];

    public AcceptedSourceResolverTests()
    {
        _work = _fileSystem.Path.GetFullPath("/work");
        _fileSystem.Directory.CreateDirectory(_work);
        _fileSystem.Directory.SetCurrentDirectory(_work);
    }

    [Fact]
    public async Task A_directory_baseline_supplies_accepted_sources_of_the_current_directory()
    {
        _fileSystem.AddFile(Work("Library", "A.cs"), "class A { public int X; }");
        var accepted = _fileSystem.Path.GetFullPath("/accepted");
        _fileSystem.AddFile(Path(accepted, "Library", "A.cs"), "class A { }");
        _fileSystem.AddFile(Path(accepted, "Library", "Old.cs"), "class Old { }");
        _fileSystem.AddFile(Path(accepted, "Library", "notes.txt"), "not source");
        _fileSystem.AddFile(Path(accepted, "Library", "obj", "Generated.cs"), "class G { }");
        _fileSystem.AddFile(Path(accepted, ".git", "Hook.cs"), "class H { }");
        var error = new StringWriter();

        var exit = await RunAsync(error, "--baseline", accepted);

        Assert.Equal(CliExitCode.Clean, exit);
        Assert.Equal("", error.ToString());
        Assert.Equal(_work, _acceptedRoot);
        Assert.Equal(
            [
                (Work("Library", "A.cs"), "class A { }", false),
                (Work("Library", "Old.cs"), "class Old { }", true),
            ],
            _acceptedFiles
        );
    }

    [Fact]
    public async Task A_git_revision_supplies_the_repository_sources_of_that_commit()
    {
        var project = Work("src", "App");
        _fileSystem.AddFile(Path(project, "A.cs"), "class A { public int X; }");
        _prefix = "src/App/";
        _tree =
        [
            ("100644 blob 1", "src/App/A.cs", "class A { }"),
            ("100644 blob 2", "src/App/Gone.CS", "class Gone { }"),
            ("100644 blob 3", "src/App/App.csproj", "<Project />"),
            ("120000 blob 4", "src/App/Link.cs", "A.cs"),
        ];
        var error = new StringWriter();

        var exit = await RunAsync(error, "--baseline", "main", Path(project, "App.csproj"));

        Assert.Equal(CliExitCode.Clean, exit);
        Assert.Equal("", error.ToString());
        Assert.Equal(
            [
                ["-C", project, "rev-parse", "--show-toplevel", "--show-prefix"],
                [
                    "-C",
                    _work,
                    "rev-parse",
                    "--verify",
                    "--quiet",
                    "--end-of-options",
                    "main^{commit}",
                ],
                ["-C", _work, "ls-tree", "-r", "-z", "--full-tree", "abc123"],
                ["-C", _work, "cat-file", "--batch"],
            ],
            _git
        );
        Assert.Equal(
            """
            1
            2

            """.ReplaceLineEndings("\n"),
            _batchInput
        );
        Assert.Equal(_work, _acceptedRoot);
        Assert.Equal(
            [
                (Path(project, "A.cs"), "class A { }", false),
                (Path(project, "Gone.CS"), "class Gone { }", true),
            ],
            _acceptedFiles
        );
    }

    [Fact]
    public async Task A_revision_without_csharp_sources_supplies_an_empty_accepted_state()
    {
        _tree = [("100644 blob 1", "README.md", "# Empty")];
        var error = new StringWriter();

        var exit = await RunAsync(error, "--baseline", "main");

        Assert.Equal(CliExitCode.Clean, exit);
        Assert.Equal("", error.ToString());
        Assert.Equal(_work, _acceptedRoot);
        Assert.Empty(_acceptedFiles);
        Assert.Null(_batchInput);
    }

    [Fact]
    public async Task A_checkout_reached_through_a_link_uses_the_repository_root()
    {
        var linked = Work("App");
        _fileSystem.Directory.CreateDirectory(linked);
        _topLevel = _fileSystem.Path.GetFullPath("/repository");
        _prefix = "App/";
        _tree = [];
        var error = new StringWriter();

        var exit = await RunAsync(error, "--baseline", "main", Path(linked, "App.csproj"));

        Assert.Equal(CliExitCode.Clean, exit);
        Assert.Equal(["-C", _work, "rev-parse", "--show-toplevel"], _git[1]);
        Assert.Equal(_topLevel, _acceptedRoot);
    }

    [Fact]
    public async Task Targets_in_one_repository_share_its_revision()
    {
        var app = Work("src", "App");
        var tool = Work("tools", "Tool");
        _fileSystem.Directory.CreateDirectory(app);
        _fileSystem.Directory.CreateDirectory(tool);
        _otherTopLevels[tool] = _work;
        _prefix = "src/App/";
        _tree = [];
        var error = new StringWriter();

        var exit = await RunAsync(
            error,
            "--baseline",
            "main",
            Path(app, "App.csproj"),
            Path(tool, "Tool.csproj"),
            Path(app, "Other.cs")
        );

        Assert.Equal((CliExitCode.Clean, ""), (exit, error.ToString()));
        Assert.Equal(
            [
                ["-C", app, "rev-parse", "--show-toplevel", "--show-prefix"],
                ["-C", tool, "rev-parse", "--show-toplevel"],
                [
                    "-C",
                    _work,
                    "rev-parse",
                    "--verify",
                    "--quiet",
                    "--end-of-options",
                    "main^{commit}",
                ],
                ["-C", _work, "ls-tree", "-r", "-z", "--full-tree", "abc123"],
            ],
            _git
        );
        Assert.Equal(_work, _acceptedRoot);
    }

    [Fact]
    public async Task Targets_in_different_repositories_fail_before_rules_run()
    {
        var other = _fileSystem.Path.GetFullPath("/other");
        _fileSystem.Directory.CreateDirectory(other);
        _otherTopLevels[other] = other;
        var error = new StringWriter();

        var exit = await RunAsync(
            error,
            "--baseline",
            "main",
            Work("App.csproj"),
            Path(other, "Other.csproj")
        );

        Assert.Equal(
            (
                CliExitCode.Failure,
                $"drillpress: Baseline 'main' needs every target in one Git repository, but '{other}' is in '{other}' and '{_work}' in '{_work}'.\n"
            ),
            (exit, error.ToString().ReplaceLineEndings("\n"))
        );
        Assert.Empty(_acceptedFiles);
    }

    [Fact]
    public async Task A_target_outside_any_repository_fails_before_rules_run()
    {
        var outside = _fileSystem.Path.GetFullPath("/outside");
        _fileSystem.Directory.CreateDirectory(outside);
        var error = new StringWriter();

        var exit = await RunAsync(
            error,
            "--baseline",
            "main",
            Work("App.csproj"),
            Path(outside, "Other.csproj")
        );

        Assert.Equal(
            (
                CliExitCode.Failure,
                $"drillpress: Baseline 'main' needs every target in a Git repository, but '{outside}' is outside one. fatal: not a git repository\n"
            ),
            (exit, error.ToString().ReplaceLineEndings("\n"))
        );
        Assert.Empty(_acceptedFiles);
    }

    [Fact]
    public async Task A_target_reaching_the_repository_by_another_path_fails_before_rules_run()
    {
        var alias = _fileSystem.Path.GetFullPath("/alias");
        _fileSystem.Directory.CreateDirectory(alias);
        _otherTopLevels[alias] = _work;
        var error = new StringWriter();

        var exit = await RunAsync(
            error,
            "--baseline",
            "main",
            Work("App.csproj"),
            Path(alias, "Other.csproj")
        );

        Assert.Equal(
            (
                CliExitCode.Failure,
                $"drillpress: Baseline 'main' needs every target under '{_work}', the root '{_work}' names; '{alias}' reaches the repository by another path.\n"
            ),
            (exit, error.ToString().ReplaceLineEndings("\n"))
        );
        Assert.Empty(_acceptedFiles);
    }

    [Fact]
    public async Task An_unknown_revision_fails_before_rules_run()
    {
        var error = new StringWriter();

        var exit = await RunAsync(error, "--baseline", "missing");

        Assert.Equal(CliExitCode.Failure, exit);
        Assert.Equal(
            "drillpress: Baseline 'missing' is neither a directory nor a Git revision.\n",
            error.ToString().ReplaceLineEndings("\n")
        );
        Assert.Empty(_acceptedFiles);
    }

    private Task<CliExitCode> RunAsync(StringWriter error, params string[] options)
    {
        string[] targets = options.Length > 2 ? options[2..] : ["target.csproj"];
        var runner = new ScriptedChildProcessRunner(
            async (executable, arguments, streams) =>
                executable switch
                {
                    "host" => await ExportAsync(arguments),
                    "git" => await GitAsync(arguments, streams),
                    _ => await CheckAsync(arguments),
                }
        );
        return new CliApplication(
            _fileSystem,
            runner,
            permissions: new StubSnapshotDirectoryPermissions(_fileSystem)
        ).RunAsync(
            [
                "check",
                "--build-host",
                "host",
                "--rules",
                "rules",
                options[0],
                options[1],
                .. targets,
            ],
            error,
            TestContext.Current.CancellationToken,
            TextWriter.Null
        );
    }

    private async Task<ChildProcessResult> ExportAsync(IReadOnlyList<string> arguments)
    {
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(
            arguments[^1],
            _fixture.Snapshot,
            TestContext.Current.CancellationToken
        );
        return new(0, "", "");
    }

    private async Task<ChildProcessResult> GitAsync(
        IReadOnlyList<string> arguments,
        ScriptedStreams? streams
    )
    {
        _git.Add(arguments.ToArray());
        var topLevel = (_topLevel ?? _work).Replace('\\', '/');
        if (arguments.Contains("--show-prefix"))
            return new(0, $"{topLevel}\n{_prefix}\n", "");
        if (arguments.Contains("--show-toplevel"))
            return _otherTopLevels.TryGetValue(arguments[1], out var other)
                ? new(0, other.Replace('\\', '/') + "\n", "")
                : new(128, "", "fatal: not a git repository");
        if (arguments.Contains("--verify"))
            return arguments[^1] == "main^{commit}" ? new(0, "abc123\n", "") : new(1, "", "");
        if (arguments.Contains("ls-tree"))
            return new(
                0,
                string.Concat(_tree.Select(entry => $"{entry.Metadata}\t{entry.Path}\0")),
                ""
            );
        _batchInput = streams!.Input;
        foreach (var line in _batchInput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var content = Encoding.UTF8.GetBytes(
                _tree.Single(entry => entry.Metadata.Split(' ')[2] == line).Text
            );
            await streams.Output.WriteAsync(
                Encoding.UTF8.GetBytes($"oid blob {content.Length}\n"),
                TestContext.Current.CancellationToken
            );
            await streams.Output.WriteAsync(content, TestContext.Current.CancellationToken);
            streams.Output.WriteByte((byte)'\n');
        }
        return new(0, "", "");
    }

    private async Task<ChildProcessResult> CheckAsync(IReadOnlyList<string> arguments)
    {
        var accepted = await new AcceptedSourcesFile(_fileSystem).ReadAsync(
            arguments[arguments.ToList().IndexOf("--baseline") + 1],
            TestContext.Current.CancellationToken
        );
        _acceptedRoot = accepted.Root;
        _acceptedFiles = accepted
            .Files.Select(file => (file.Path, Encoding.UTF8.GetString(file.Content), file.Removed))
            .ToArray();
        var response = _fixture.Response with
        {
            Contexts = _fixture
                .Response.Contexts.Select(context => context with { Findings = [] })
                .ToArray(),
            Batches = [],
        };
        return new(0, Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response)), "");
    }

    private string Work(params string[] segments) => Path(_work, segments);

    private string Path(string root, params string[] segments) =>
        _fileSystem.Path.Combine([root, .. segments]);
}
