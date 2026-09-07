using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Manifest;

public sealed class CompilationSnapshotFileTests : IntegrationTest
{
    [Fact]
    public async Task Replaces_an_existing_snapshot_after_closing_the_temporary_file()
    {
        var directory = CreateTemporaryDirectory("drillpress-snapshot-");
        var path = FileSystem.Path.Combine(directory.FullName, "snapshot.json");
        await FileSystem.File.WriteAllTextAsync(path, "previous snapshot", TestContext.Current.CancellationToken);
        var storage = new CompilationSnapshotFile();

        await storage.WriteAsync(path, CompilationSnapshot.Create(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"fileIdentifier":"drillpress-compilation","formatVersion":1,"projects":[]}""",
            await FileSystem.File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([path], FileSystem.Directory.GetFiles(directory.FullName));
    }

    [Fact]
    public async Task Failed_replacement_removes_the_temporary_file()
    {
        var directory = CreateTemporaryDirectory("drillpress-snapshot-");
        var path = FileSystem.Path.Combine(directory.FullName, "snapshot.json");
        FileSystem.Directory.CreateDirectory(path);
        var storage = new CompilationSnapshotFile();

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            storage.WriteAsync(path, CompilationSnapshot.Create(), TestContext.Current.CancellationToken));

        Assert.Contains(exception.GetType(), new[] { typeof(IOException), typeof(UnauthorizedAccessException) });
        Assert.True(FileSystem.Directory.Exists(path));
        Assert.Empty(FileSystem.Directory.GetFiles(directory.FullName));
    }
}
