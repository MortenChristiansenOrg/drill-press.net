using DrillPress.Release;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class StubNugetFeed() : NugetFeed(null!)
{
    public Dictionary<string, IReadOnlyDictionary<string, string>> Published { get; } = [];
    public List<string> Pushed { get; } = [];
    public string? FailPackage { get; set; }
    public bool IndexPushes { get; set; } = true;

    public override Task<IReadOnlyDictionary<string, string>?> ReadAsync(
        ReleasePackage package,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Published.GetValueOrDefault(package.Id));
    }

    public override Task PushAsync(
        ReleasePackage package,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        if (package.Id == FailPackage)
        {
            throw new InvalidOperationException("Feed unavailable.");
        }

        Pushed.Add(package.Id);
        if (IndexPushes)
        {
            Published.Add(package.Id, package.Contents);
        }

        return Task.CompletedTask;
    }
}
