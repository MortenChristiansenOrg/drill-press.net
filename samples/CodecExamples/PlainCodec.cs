namespace CodecExamples;

public class PlainCodec : ITextCodec
{
    public string Encode(string text) => text;
}
