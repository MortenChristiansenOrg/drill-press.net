using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class CompilationSnapshotTests : SnapshotTest
{
    [Fact]
    public async Task Round_trips_the_current_snapshot_format()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expectedProject = TestSnapshots.CreateProject("Example.cs", "public class Example { }");
        var snapshotPath = await WriteSnapshotAsync(
            CompilationSnapshot.Create(expectedProject),
            cancellationToken);

        var snapshot = await CompilationSnapshot.ReadAsync(snapshotPath, cancellationToken);

        Assert.Equal(CompilationSnapshot.ExpectedFileIdentifier, snapshot.FileIdentifier);
        Assert.Equal(CompilationSnapshot.CurrentFormatVersion, snapshot.FormatVersion);
        var project = Assert.Single(snapshot.Projects);
        Assert.Equal(expectedProject.Name, project.Name);
        Assert.Equal(expectedProject.ProjectPath, project.ProjectPath);
        Assert.Equal(expectedProject.MetadataReferences, project.MetadataReferences);
        Assert.Equal(expectedProject.Documents, project.Documents);
    }

    [Fact]
    public async Task Write_rejects_an_unknown_file_identifier()
    {
        var snapshot = new CompilationSnapshot("unknown", 1, []);
        var snapshotPath = CreateTemporaryPath(".json");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            snapshot.WriteAsync(snapshotPath, TestContext.Current.CancellationToken));

        Assert.Contains("not a Drill Press", exception.Message);
    }

    [Fact]
    public async Task Read_rejects_an_unsupported_format_version()
    {
        var snapshotPath = CreateTemporaryPath(".json");
        await File.WriteAllTextAsync(
            snapshotPath,
            """
            {"fileIdentifier":"drillpress-compilation","formatVersion":2,"projects":[]}
            """,
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CompilationSnapshot.ReadAsync(snapshotPath, TestContext.Current.CancellationToken));

        Assert.Contains("format 2 is not supported", exception.Message);
    }
}
