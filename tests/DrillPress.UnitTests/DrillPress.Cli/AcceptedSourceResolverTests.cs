using System.Formats.Tar;
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
        var error = new StringWriter();

        var exit = await RunAsync(error, "--baseline", "main", Path(project, "App.csproj"));

        Assert.Equal(CliExitCode.Clean, exit);
        Assert.Equal("", error.ToString());
        Assert.Equal(
            [
                ["-C", project, "rev-parse", "--show-cdup"],
                [
                    "-C",
                    _work,
                    "rev-parse",
                    "--verify",
                    "--quiet",
                    "--end-of-options",
                    "main^{commit}",
                ],
                [
                    "-C",
                    _work,
                    "archive",
                    "--format=tar",
                    "-o",
                    "<archive>",
                    "abc123",
                    "--",
                    ":(glob)**/*.cs",
                ],
            ],
            _git
        );
        Assert.Equal(_work, _acceptedRoot);
        Assert.Equal(
            [
                (Path(project, "A.cs"), "class A { }", false),
                (Path(project, "Gone.cs"), "class Gone { }", true),
            ],
            _acceptedFiles
        );
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
            async (executable, arguments) =>
                executable switch
                {
                    "host" => await ExportAsync(arguments),
                    "git" => await GitAsync(arguments),
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

    private async Task<ChildProcessResult> GitAsync(IReadOnlyList<string> arguments)
    {
        var archive = arguments.Contains("archive")
            ? arguments[arguments.ToList().IndexOf("-o") + 1]
            : null;
        _git.Add(
            arguments.Select(argument => argument == archive ? "<archive>" : argument).ToArray()
        );
        if (arguments.Contains("--show-cdup"))
            return new(0, "../../\n", "");
        if (arguments.Contains("--verify"))
            return arguments[^1] == "main^{commit}" ? new(0, "abc123\n", "") : new(1, "", "");
        await using var stream = _fileSystem.File.Create(archive!);
        await using var writer = new TarWriter(stream);
        await writer.WriteEntryAsync(
            new PaxGlobalExtendedAttributesTarEntry(
                new Dictionary<string, string> { ["comment"] = "abc123" }
            ),
            TestContext.Current.CancellationToken
        );
        await writer.WriteEntryAsync(
            new PaxTarEntry(TarEntryType.Directory, "src/App/"),
            TestContext.Current.CancellationToken
        );
        foreach (
            var (name, text) in new[]
            {
                ("src/App/A.cs", "class A { }"),
                ("src/App/Gone.cs", "class Gone { }"),
            }
        )
            await writer.WriteEntryAsync(
                new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)),
                },
                TestContext.Current.CancellationToken
            );
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
