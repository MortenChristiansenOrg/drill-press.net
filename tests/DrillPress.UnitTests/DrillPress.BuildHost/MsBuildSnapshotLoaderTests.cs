using System.IO.Abstractions.TestingHelpers;
using DrillPress.BuildHost;
using Xunit;

namespace DrillPress.UnitTests.BuildHost;

public sealed class MsBuildSnapshotLoaderTests
{
    [Fact]
    public void Public_construction_requires_no_external_dependencies()
    {
        var type = typeof(MsBuildSnapshotLoader);

        var constructors = type.GetConstructors();

        Assert.Equal([0], constructors.Select(constructor => constructor.GetParameters().Length));
    }

    [Fact]
    public async Task A_solution_beside_other_targets_fails_before_loading()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("App.slnx", new MockFileData("<Solution />"));
        fileSystem.AddFile("Program.cs", new MockFileData("class Program;"));
        var loader = new MsBuildSnapshotLoader(fileSystem);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            loader.LoadCompilationsAsync(
                ["Program.cs", "App.slnx"],
                new SnapshotLoadOptions(),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(
            $"Solution target '{fileSystem.Path.GetFullPath("App.slnx")}' must be the only target; check it on its own or list its projects.",
            error.Message
        );
    }
}
