using System.Buffers;
using System.Text;

namespace CodecExamples;

public class PooledUtf8Decoder
{
    // Deliberate ownership bug: the returned callback can run after another caller rents these bytes.
    public Func<string> ReadDeferred(Stream input)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            var count = input.Read(bytes, 0, bytes.Length);
            return () => Encoding.UTF8.GetString(bytes, 0, count);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }
}
