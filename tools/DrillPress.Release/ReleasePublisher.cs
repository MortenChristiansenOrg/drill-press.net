namespace DrillPress.Release;

public sealed class ReleasePublisher
{
    private readonly NugetFeed _feed;
    private readonly int _attempts;
    private readonly TimeSpan _retryDelay;

    public ReleasePublisher(NugetFeed feed)
        : this(feed, 60, TimeSpan.FromSeconds(10)) { }

    internal ReleasePublisher(NugetFeed feed, int attempts, TimeSpan retryDelay)
    {
        _feed = feed;
        _attempts = attempts;
        _retryDelay = retryDelay;
    }

    public async Task PublishAsync(
        IReadOnlyList<ReleasePackage> packages,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var missing = new List<ReleasePackage>();
        foreach (var package in packages)
        {
            if (!Matches(package, await _feed.ReadAsync(package, cancellationToken)))
            {
                missing.Add(package);
            }
        }

        foreach (var package in missing)
        {
            await _feed.PushAsync(package, apiKey, cancellationToken);
        }

        foreach (var package in packages)
        {
            await RequirePublishedAsync(package, cancellationToken);
        }
    }

    private async Task RequirePublishedAsync(
        ReleasePackage package,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 0; attempt < _attempts; attempt++)
        {
            if (Matches(package, await _feed.ReadAsync(package, cancellationToken)))
            {
                return;
            }

            if (attempt + 1 < _attempts)
            {
                await Task.Delay(_retryDelay, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"{package.Id} {package.Version} is not available on nuget.org. Rerun this release after indexing completes."
        );
    }

    private static bool Matches(ReleasePackage package, IReadOnlyDictionary<string, string>? remote)
    {
        if (remote is null)
        {
            return false;
        }

        if (
            remote.Count != package.Contents.Count
            || package.Contents.Any(entry =>
                !remote.TryGetValue(entry.Key, out var hash) || hash != entry.Value
            )
        )
        {
            throw new InvalidDataException(
                $"Published contents differ for {package.Id} {package.Version}; existing versions cannot be overwritten."
            );
        }

        return true;
    }
}
