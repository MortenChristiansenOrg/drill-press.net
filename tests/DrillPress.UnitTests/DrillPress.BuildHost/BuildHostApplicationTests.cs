using System.IO.Abstractions.TestingHelpers;
using DrillPress.BuildHost;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.BuildHost;

public sealed class BuildHostApplicationTests
{
    [Fact]
    public void Public_construction_requires_no_external_dependencies()
    {
        var type = typeof(BuildHostApplication);

        var constructors = type.GetConstructors();

        Assert.Equal([0], constructors.Select(constructor => constructor.GetParameters().Length));
    }

    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public async Task Run_returns_failure_and_usage_for_invalid_arguments()
    {
        var error = new StringWriter();

        var exitCode = await new BuildHostApplication(_fileSystem, new StubSnapshotLoader(_fileSystem)).RunAsync(
            ["export"],
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(BuildHostExitCode.Failure, exitCode);
        Assert.Equal(
            $"Usage: DrillPress.BuildHost export <project.csproj> <snapshot>{Environment.NewLine}",
            error.ToString());
    }

    [Fact]
    public async Task Export_creates_parent_directories_and_persists_the_loaded_snapshot()
    {
        _fileSystem.AddFile("Target.csproj", new MockFileData("<Project />"));
        var loader = new StubSnapshotLoader(_fileSystem);
        var application = new BuildHostApplication(_fileSystem, loader);

        await application.ExportAsync("Target.csproj", "nested/output/snapshot.json", TestContext.Current.CancellationToken);

        Assert.Equal(_fileSystem.Path.GetFullPath("Target.csproj"), loader.ProjectPath);
        Assert.Equal(
            """{"fileIdentifier":"drillpress-compilation","formatVersion":1,"projects":[]}""",
            _fileSystem.File.ReadAllText("nested/output/snapshot.json"));
    }

    [Theory]
    [InlineData("Missing.csproj")]
    [InlineData("Target.txt")]
    public async Task Invalid_project_paths_do_not_invoke_the_external_loader(string path)
    {
        _fileSystem.AddFile("Target.txt", new MockFileData("<Project />"));
        var loader = new StubSnapshotLoader(_fileSystem);
        var application = new BuildHostApplication(_fileSystem, loader);

        var error = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            application.ExportAsync(path, "snapshot.json", TestContext.Current.CancellationToken));

        Assert.Equal($"C# project '{path}' was not found.", error.Message);
        Assert.Equal(_fileSystem.Path.GetFullPath(path), error.FileName);
        Assert.Null(loader.ProjectPath);
        Assert.False(_fileSystem.File.Exists("snapshot.json"));
    }

    [Fact]
    public async Task Loader_failure_preserves_existing_output_and_reports_the_error()
    {
        _fileSystem.AddFile("Target.csproj", new MockFileData("<Project />"));
        _fileSystem.AddFile("snapshot.json", new MockFileData("untouched"));
        var loader = new StubSnapshotLoader(_fileSystem) { Failure = new InvalidOperationException("SDK failed.") };
        var application = new BuildHostApplication(_fileSystem, loader);
        var error = new StringWriter();

        var result = await application.RunAsync(
            ["export", "Target.csproj", "snapshot.json"], error, TestContext.Current.CancellationToken);

        Assert.Equal(BuildHostExitCode.Failure, result);
        Assert.Equal($"DrillPress.BuildHost: SDK failed.{Environment.NewLine}", error.ToString());
        Assert.Equal("untouched", _fileSystem.File.ReadAllText("snapshot.json"));
    }
}
