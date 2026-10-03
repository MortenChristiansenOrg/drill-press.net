using System.IO.Abstractions.TestingHelpers;
using System.Text;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class ComponentVersionTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("")]
    public void Snapshot_rejects_a_different_release(string version)
    {
        var snapshot = CompilationSnapshot.Create() with { ProductVersion = version };

        var error = Assert.Throws<InvalidDataException>(() =>
            SnapshotValidation.Validate(snapshot)
        );

        Assert.Equal(Message(version, "Snapshot producer"), error.Message);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("")]
    public void Response_rejects_a_different_release(string version)
    {
        var snapshot = CompilationSnapshot.Create();
        var response = new BundleResponse(
            BundleResponseProtocol.CurrentVersion,
            snapshot.RequestId,
            [],
            []
        )
        {
            ProductVersion = version,
        };
        var json = Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response));

        var error = Assert.Throws<InvalidDataException>(() =>
            BundleResponseProtocol.Read(json, snapshot)
        );

        Assert.Equal(Message(version, "Rule bundle"), error.Message);
    }

    [Fact]
    public async Task Snapshot_version_is_checked_before_deserializing_the_payload()
    {
        var version = ComponentVersion.Current + "-different";
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(
            "snapshot.json",
            new MockFileData(
                $$"""{"fileIdentifier":"drillpress-compilation","formatVersion":5,"productVersion":"{{version}}","projects":"incompatible shape"}"""
            )
        );

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CompilationSnapshotFile(fileSystem).ReadAsync(
                "snapshot.json",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(Message(version, "Snapshot producer"), error.Message);
    }

    [Fact]
    public void Missing_response_version_explains_how_to_upgrade()
    {
        var snapshot = CompilationSnapshot.Create();

        var error = Assert.Throws<InvalidDataException>(() =>
            BundleResponseProtocol.Read(
                """{"protocolVersion":1,"requestId":"old","contexts":[],"batches":[]}""",
                snapshot
            )
        );

        Assert.Equal(Message("missing", "Rule bundle"), error.Message);
    }

    private static string Message(string version, string component) =>
        $"{component} version '{version}' is incompatible with Drill Press {ComponentVersion.Current}. Install tool and SDK packages at {ComponentVersion.Current} and rebuild the rule bundle. Releases require exact version matching.";
}
