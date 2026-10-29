using System.IO.Abstractions.TestingHelpers;
using DrillPress.BuildHost;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.BuildHost;

public sealed class MsBuildSnapshotLoaderTests
{
    [Fact]
    public async Task Reuses_unchanged_capture_with_a_new_request_identity()
    {
        var files = Files();
        var loader = new FakeCaptureSnapshotLoader(files);
        var options = new SnapshotLoadOptions { ReuseInputs = true };
        var first = await loader.LoadCompilationsAsync(
            "/repo/Target.csproj",
            options,
            TestContext.Current.CancellationToken
        );

        var repeated = await loader.LoadCompilationsAsync(
            "/repo/Target.csproj",
            options,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, loader.Captures);
        Assert.NotEqual(first.Snapshot.RequestId, repeated.Snapshot.RequestId);
        Assert.Equal(first.Snapshot.Projects[0].Documents, repeated.Snapshot.Projects[0].Documents);
        Assert.Same(first.Contexts[0].Compilation, repeated.Contexts[0].Compilation);
    }

    [Theory]
    [InlineData("/repo/Source.cs", "class D {}")]
    [InlineData("/repo/New.cs", "class Added {}")]
    [InlineData("/shared/input.txt", "changed")]
    [InlineData("/resources/new.json", "{}")]
    public async Task Invalidates_changed_inputs_and_directory_inventories(
        string path,
        string content
    )
    {
        var files = Files();
        var loader = new FakeCaptureSnapshotLoader(files);
        var options = new SnapshotLoadOptions { ReuseInputs = true };
        await loader.LoadCompilationsAsync(
            "/repo/Target.csproj",
            options,
            TestContext.Current.CancellationToken
        );
        files.AddFile(path, content);

        var updated = await loader.LoadCompilationsAsync(
            "/repo/Target.csproj",
            options,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, loader.Captures);
        Assert.Equal(
            files.File.ReadAllText("/repo/Source.cs"),
            updated.Snapshot.Projects[0].Documents[0].Text
        );
    }

    [Fact]
    public async Task Invalidates_deleted_inputs_and_changed_build_options()
    {
        var files = Files();
        var loader = new FakeCaptureSnapshotLoader(files);
        var options = new SnapshotLoadOptions { ReuseInputs = true };
        await loader.LoadCompilationsAsync(
            "/repo/Target.csproj",
            options,
            TestContext.Current.CancellationToken
        );
        files.File.Delete("/shared/input.txt");

        await loader.LoadCompilationsAsync(
            "/repo/Target.csproj",
            options,
            TestContext.Current.CancellationToken
        );
        await loader.LoadCompilationsAsync(
            "/repo/Target.csproj",
            options with
            {
                IncludeReferencedProjects = true,
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, loader.Captures);
    }

    private static MockFileSystem Files() =>
        new(
            new Dictionary<string, MockFileData>
            {
                ["/repo/Target.csproj"] = new("<Project />"),
                ["/repo/Source.cs"] = new("class C {}"),
                ["/shared/input.txt"] = new("original"),
                ["/resources/old.json"] = new("{}"),
            }
        );

    [Fact]
    public void Public_construction_requires_no_external_dependencies()
    {
        var type = typeof(MsBuildSnapshotLoader);

        var constructors = type.GetConstructors();

        Assert.Equal([0], constructors.Select(constructor => constructor.GetParameters().Length));
    }
}
