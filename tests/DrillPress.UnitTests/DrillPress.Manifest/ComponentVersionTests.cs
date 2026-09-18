using System.IO.Abstractions.TestingHelpers;
using System.Text;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class ComponentVersionTests
{
    [Theory]
    [InlineData("0.0.0")]
    [InlineData("0.0.999")]
    [InlineData("")]
    public void Snapshot_rejects_a_different_alpha_release(string version)
    {
        var snapshot = CompilationSnapshot.Create() with { ProductVersion = version };

        var error = Assert.Throws<InvalidDataException>(() =>
            SnapshotValidation.Validate(snapshot)
        );

        Assert.Equal(Message(version, "Snapshot producer"), error.Message);
    }

    [Theory]
    [InlineData("0.0.0")]
    [InlineData("0.0.999")]
    [InlineData("")]
    public void Response_rejects_a_different_alpha_release(string version)
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
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(
            "snapshot.json",
            new MockFileData(
                """{"fileIdentifier":"drillpress-compilation","formatVersion":4,"productVersion":"0.0.999","projects":"incompatible shape"}"""
            )
        );

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CompilationSnapshotFile(fileSystem).ReadAsync(
                "snapshot.json",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(Message("0.0.999", "Snapshot producer"), error.Message);
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
        $"{component} version '{version}' is incompatible with Drill Press {ComponentVersion.Current}. Install tool and SDK packages at {ComponentVersion.Current} and rebuild the rule bundle. Alpha releases require exact version matching.";
}
