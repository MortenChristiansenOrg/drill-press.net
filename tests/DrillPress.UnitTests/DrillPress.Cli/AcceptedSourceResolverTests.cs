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
                ["-C", _work, "cat-file", "--batch", "--filters"],
            ],
            _git
        );
        Assert.Equal("1 src/App/A.cs\n2 src/App/Gone.CS\n", _batchInput);
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
        var linked = Work("linked");
        _fileSystem.Directory.CreateDirectory(linked);
        _topLevel = _fileSystem.Path.GetFullPath("/repository");
        _prefix = "src/App/";
        _tree = [];
        var error = new StringWriter();

        var exit = await RunAsync(error, "--baseline", "main", Path(linked, "App.csproj"));

        Assert.Equal(CliExitCode.Clean, exit);
        Assert.Equal(_topLevel, _acceptedRoot);
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
        var target = options.Length == 3 ? options[2] : "target.csproj";
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
            ["check", "--build-host", "host", "--rules", "rules", options[0], options[1], target],
            error,
            TestContext.Current.CancellationToken,
            TextWriter.Null
        );
    }

    private async Task<ChildProcessResult> ExportAsync(IReadOnlyList<string> arguments)
    {
        await new CompilationSnapshotFile(_fileSystem).WriteAsync(
            arguments[2],
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
        if (arguments.Contains("--show-prefix"))
            return new(0, $"{(_topLevel ?? _work).Replace('\\', '/')}\n{_prefix}\n", "");
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
                _tree.Single(entry => entry.Metadata.Split(' ')[2] + " " + entry.Path == line).Text
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
