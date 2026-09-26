using DrillPress.Release;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Release;

public sealed class ReleasePublisherTests
{
    private readonly ReleasePackage[] _packages = PackageInventory
        .PackageIds.Select(id => new ReleasePackage(
            id,
            "1.0.0-rc.1",
            id + ".nupkg",
            new Dictionary<string, string> { [id + ".dll"] = "hash" }
        ))
        .ToArray();
    private readonly StubNugetFeed _feed = new();

    [Fact]
    public async Task Publishes_all_packages_and_reruns_without_uploads()
    {
        var publisher = CreatePublisher();

        await publisher.PublishAsync(_packages, "key", TestContext.Current.CancellationToken);
        await publisher.PublishAsync(_packages, "key", TestContext.Current.CancellationToken);

        Assert.Equal(PackageInventory.PackageIds, _feed.Pushed);
        Assert.Equal(PackageInventory.PackageIds, _feed.Published.Keys);
    }

    [Fact]
    public async Task Resumes_after_partial_publication()
    {
        _feed.FailPackage = "DrillPress.Engine";
        var publisher = CreatePublisher();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            publisher.PublishAsync(_packages, "key", TestContext.Current.CancellationToken)
        );
        _feed.FailPackage = null;

        await publisher.PublishAsync(_packages, "key", TestContext.Current.CancellationToken);

        Assert.Equal(PackageInventory.PackageIds, _feed.Pushed);
        Assert.Equal(PackageInventory.PackageIds, _feed.Published.Keys);
    }

    [Fact]
    public async Task Conflicting_existing_package_prevents_any_upload()
    {
        _feed.Published.Add(
            "DrillPress.Cli",
            new Dictionary<string, string> { ["different.dll"] = "different" }
        );
        var publisher = CreatePublisher();

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            publisher.PublishAsync(_packages, "key", TestContext.Current.CancellationToken)
        );

        Assert.Empty(_feed.Pushed);
        Assert.Equal(
            "Published contents differ for DrillPress.Cli 1.0.0-rc.1; existing versions cannot be overwritten.",
            error.Message
        );
    }

    [Fact]
    public async Task Missing_indexed_package_cannot_report_success()
    {
        _feed.IndexPushes = false;
        var publisher = CreatePublisher();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            publisher.PublishAsync(_packages, "key", TestContext.Current.CancellationToken)
        );

        Assert.Equal(PackageInventory.PackageIds, _feed.Pushed);
        Assert.Equal(
            "DrillPress.Manifest 1.0.0-rc.1 is not available on nuget.org. Rerun this release after indexing completes.",
            error.Message
        );
    }

    [Fact]
    public async Task Cancellation_prevents_publication()
    {
        var publisher = CreatePublisher();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            publisher.PublishAsync(_packages, "key", new CancellationToken(true))
        );

        Assert.Empty(_feed.Pushed);
    }

    private ReleasePublisher CreatePublisher() => new(_feed, 2, TimeSpan.Zero);
}
