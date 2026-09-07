using System.IO.Abstractions.TestingHelpers;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class CompilationSnapshotFileTests
{
    [Fact]
    public void Public_construction_requires_no_external_dependencies()
    {
        var type = typeof(CompilationSnapshotFile);

        var constructors = type.GetConstructors();

        Assert.Equal([0], constructors.Select(constructor => constructor.GetParameters().Length));
    }

    private readonly MockFileSystem _fileSystem = new();
    private const string SnapshotPath = "snapshot.json";

    [Fact]
    public async Task Writes_the_complete_envelope_using_the_injected_filesystem()
    {
        var storage = new CompilationSnapshotFile(_fileSystem);
        var snapshot = CompilationSnapshot.Create();

        await storage.WriteAsync(SnapshotPath, snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"fileIdentifier":"drillpress-compilation","formatVersion":1,"projects":[]}""",
            _fileSystem.File.ReadAllText(SnapshotPath));
    }

    [Fact]
    public async Task Null_json_reports_the_requested_snapshot_path()
    {
        _fileSystem.AddFile(SnapshotPath, new MockFileData("null"));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CompilationSnapshotFile(_fileSystem).ReadAsync(SnapshotPath, TestContext.Current.CancellationToken));

        Assert.Equal("Compilation snapshot 'snapshot.json' is empty.", error.Message);
    }

    [Fact]
    public async Task Round_trips_the_current_snapshot_format_in_memory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expectedProject = TestSnapshots.CreateProject("Example.cs", "public class Example { }") with
        {
            PreprocessorSymbols = ["FEATURE"],
            ProjectReferences = [new MetadataImageSnapshot([1, 2, 3], ["Dependency"], false)],
        };
        var expected = CompilationSnapshot.Create(expectedProject);
        var storage = new CompilationSnapshotFile(_fileSystem);
        await storage.WriteAsync(SnapshotPath, expected, cancellationToken);

        var snapshot = await storage.ReadAsync(SnapshotPath, cancellationToken);

        Assert.Equal(CompilationSnapshot.ExpectedFileIdentifier, snapshot.FileIdentifier);
        Assert.Equal(CompilationSnapshot.CurrentFormatVersion, snapshot.FormatVersion);
        var project = Assert.Single(snapshot.Projects);
        Assert.Equal(expectedProject.Name, project.Name);
        Assert.Equal(expectedProject.AssemblyName, project.AssemblyName);
        Assert.Equal(expectedProject.ProjectPath, project.ProjectPath);
        Assert.Equal(expectedProject.LanguageVersion, project.LanguageVersion);
        Assert.Equal(expectedProject.OutputKind, project.OutputKind);
        Assert.Equal(expectedProject.NullableContextOptions, project.NullableContextOptions);
        Assert.Equal(expectedProject.PreprocessorSymbols, project.PreprocessorSymbols);
        Assert.Equal(expectedProject.MetadataReferences, project.MetadataReferences);
        Assert.Equal(expectedProject.Documents, project.Documents);
        var reference = Assert.Single(project.ProjectReferences);
        Assert.Equal(expectedProject.ProjectReferences[0].Image, reference.Image);
        Assert.Equal(expectedProject.ProjectReferences[0].Aliases, reference.Aliases);
        Assert.Equal(expectedProject.ProjectReferences[0].EmbedInteropTypes, reference.EmbedInteropTypes);
    }

    [Theory]
    [InlineData("""{"fileIdentifier":"drillpress-compilation","formatVersion":1}""")]
    [InlineData("""{"fileIdentifier":"drillpress-compilation","formatVersion":1,"projects":null}""")]
    public async Task Read_rejects_missing_or_null_projects(string json)
    {
        _fileSystem.AddFile(SnapshotPath, new MockFileData(json));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CompilationSnapshotFile(_fileSystem).ReadAsync(SnapshotPath, TestContext.Current.CancellationToken));

        Assert.Equal("Compilation snapshot must contain a projects array.", exception.Message);
    }

    [Fact]
    public async Task Write_rejects_an_unknown_file_identifier_in_memory()
    {
        var snapshot = new CompilationSnapshot("unknown", 1, []);
        _fileSystem.AddFile(SnapshotPath, new MockFileData("untouched"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CompilationSnapshotFile(_fileSystem).WriteAsync(SnapshotPath, snapshot, TestContext.Current.CancellationToken));

        Assert.Equal("The input is not a Drill Press compilation snapshot.", exception.Message);
        Assert.Equal("untouched", _fileSystem.File.ReadAllText(SnapshotPath));
    }

    [Fact]
    public async Task Read_rejects_an_unsupported_format_version_in_memory()
    {
        _fileSystem.AddFile(SnapshotPath, new MockFileData(
            """
            {"fileIdentifier":"drillpress-compilation","formatVersion":2,"projects":[]}
            """));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CompilationSnapshotFile(_fileSystem).ReadAsync(SnapshotPath, TestContext.Current.CancellationToken));

        Assert.Equal("Compilation snapshot format 2 is not supported; expected 1.", exception.Message);
    }
}
