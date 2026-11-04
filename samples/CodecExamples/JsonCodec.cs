namespace CodecExamples;

internal class JsonCodec : ITextCodec
{
    public string Encode(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    public async Task SendWithRetryAsync(string text, Func<string, Task> send)
    {
        var encoded = Encode(text);
        await RetryOnTimeoutAsync(() => send(encoded), WaitBeforeRetry);
    }

    private static async Task RetryOnTimeoutAsync(Func<Task> attempt, Action backoff)
    {
        try
        {
            await attempt();
        }
        catch (TimeoutException)
        {
            backoff();
            await attempt();
        }
    }

    // Deliberately blocking backoff: callers should await Task.Delay instead.
    private static void WaitBeforeRetry() => Thread.Sleep(TimeSpan.FromMilliseconds(100));
}
