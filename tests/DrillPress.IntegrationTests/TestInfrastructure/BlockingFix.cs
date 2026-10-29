namespace DrillPress.IntegrationTests.TestInfrastructure;

internal sealed class BlockingFix : IDisposable
{
    private readonly TaskCompletionSource _started = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly TaskCompletionSource _release = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    internal Task Started => _started.Task;

    internal RuleCatalog Rules()
    {
        var rules = new RuleCatalog();
        rules
            .Rule("BLOCK", "Test cancellation.")
            .For(Code.Files)
            .Forbid(fix: file =>
            {
                _started.TrySetResult();
                _release.Task.GetAwaiter().GetResult();
                return null;
            });
        return rules;
    }

    public void Dispose() => _release.TrySetResult();
}
