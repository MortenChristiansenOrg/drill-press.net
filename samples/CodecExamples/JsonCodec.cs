namespace CodecExamples;

internal class JsonCodec : ITextCodec
{
    public string Encode(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    public async Task<string> EncodeLater(string text)
    {
        Pause();
        await Task.CompletedTask;
        return Encode(text);
    }

    private void Pause() => Thread.Sleep(1);
}
