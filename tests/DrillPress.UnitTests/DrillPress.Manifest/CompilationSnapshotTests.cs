using System.Text;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class CompilationSnapshotTests
{
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
        await using var stream = new MemoryStream();
        await expected.WriteAsync(stream, cancellationToken);
        stream.Position = 0;

        var snapshot = await CompilationSnapshot.ReadAsync(stream, cancellationToken);

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
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CompilationSnapshot.ReadAsync(stream, TestContext.Current.CancellationToken));

        Assert.Equal("Compilation snapshot must contain a projects array.", exception.Message);
    }

    [Fact]
    public async Task Write_rejects_an_unknown_file_identifier_in_memory()
    {
        var snapshot = new CompilationSnapshot("unknown", 1, []);
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            snapshot.WriteAsync(stream, TestContext.Current.CancellationToken));

        Assert.Contains("not a Drill Press", exception.Message);
    }

    [Fact]
    public async Task Read_rejects_an_unsupported_format_version_in_memory()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """
            {"fileIdentifier":"drillpress-compilation","formatVersion":2,"projects":[]}
            """));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CompilationSnapshot.ReadAsync(stream, TestContext.Current.CancellationToken));

        Assert.Contains("format 2 is not supported", exception.Message);
    }
}
