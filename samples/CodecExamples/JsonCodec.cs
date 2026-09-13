namespace CodecExamples;

internal class JsonCodec : ITextCodec
{
    public string Encode(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    public async Task SendWithRetryAsync(string text, Func<string, Task> send)
    {
        var encoded = Encode(text);
        try
        {
            await send(encoded);
        }
        catch (TimeoutException)
        {
            WaitBeforeRetry();
            await send(encoded);
        }
    }

    // Deliberately blocking backoff: callers should await Task.Delay instead.
    private static void WaitBeforeRetry() => Thread.Sleep(TimeSpan.FromMilliseconds(100));
}
