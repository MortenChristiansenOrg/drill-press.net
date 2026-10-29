using System.Text;

namespace DrillPress.Manifest;

/// <summary>Bounds worker console capture before serializing its response.</summary>
internal sealed class BoundedStringWriter(int maximumBytes, string name)
    : StringWriter(System.Globalization.CultureInfo.InvariantCulture)
{
    private readonly int _maximumBytes = maximumBytes;
    private readonly string _name = name;
    private int _bytes;
    private bool _disposed;

    public override void Write(char value) => Write(value.ToString().AsSpan());

    public override void Write(string? value) => Write(value.AsSpan());

    public override void Write(char[] buffer, int index, int count) =>
        Write(buffer.AsSpan(index, count));

    public override void Write(ReadOnlySpan<char> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = Encoding.UTF8.GetByteCount(buffer);
        if (bytes > _maximumBytes - _bytes)
            throw new InvalidDataException($"Child {_name} exceeded {_maximumBytes} bytes.");
        _bytes += bytes;
        GetStringBuilder().Append(buffer);
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
