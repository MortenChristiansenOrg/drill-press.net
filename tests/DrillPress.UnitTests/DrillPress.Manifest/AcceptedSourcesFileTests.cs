using System.IO.Abstractions.TestingHelpers;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class AcceptedSourcesFileTests
{
    [Fact]
    public void Public_construction_requires_no_external_dependencies()
    {
        var type = typeof(AcceptedSourcesFile);

        var constructors = type.GetConstructors();

        Assert.Equal([0], constructors.Select(constructor => constructor.GetParameters().Length));
    }

    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public async Task Writes_the_complete_envelope_using_the_injected_filesystem()
    {
        var storage = new AcceptedSourcesFile(_fileSystem);

        await storage.WriteAsync(
            "accepted.json",
            AcceptedSources.Create(
                "/repository",
                new AcceptedSourceFile("/repository/A.cs", [65], true)
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            $$"""{"fileIdentifier":"drillpress-accepted-sources","formatVersion":1,"root":"/repository","files":[{"path":"/repository/A.cs","content":"QQ==","removed":true}],"productVersion":"{{ComponentVersion.Current}}"}""",
            _fileSystem.File.ReadAllText("accepted.json")
        );
    }

    [Fact]
    public async Task Reads_the_files_it_wrote()
    {
        var storage = new AcceptedSourcesFile(_fileSystem);
        await storage.WriteAsync(
            "accepted.json",
            AcceptedSources.Create(
                "/repository",
                new AcceptedSourceFile("/repository/A.cs", [65], false)
            ),
            TestContext.Current.CancellationToken
        );

        var accepted = await storage.ReadAsync(
            "accepted.json",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("/repository", accepted.Root);
        Assert.Equal(
            [("/repository/A.cs", "QQ==", false)],
            accepted.Files.Select(file =>
                (file.Path, Convert.ToBase64String(file.Content), file.Removed)
            )
        );
    }

    [Fact]
    public async Task Rejects_state_from_another_product_version()
    {
        _fileSystem.AddFile(
            "accepted.json",
            new MockFileData(
                """{"fileIdentifier":"drillpress-accepted-sources","formatVersion":1,"root":"/repository","files":[],"productVersion":"0.0.0-other"}"""
            )
        );
        var storage = new AcceptedSourcesFile(_fileSystem);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            storage.ReadAsync("accepted.json", TestContext.Current.CancellationToken)
        );

        Assert.Equal(
            $"Accepted source producer version '0.0.0-other' is incompatible with Drill Press {ComponentVersion.Current}. Install tool and SDK packages at {ComponentVersion.Current} and rebuild the rule bundle. Releases require exact version matching.",
            exception.Message
        );
    }
}
