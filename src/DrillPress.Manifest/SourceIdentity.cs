using System.Security.Cryptography;
using System.Text;

namespace DrillPress.Manifest;

/// <summary>Verifies source text and byte fingerprints without compiler dependencies.</summary>
public static class SourceIdentity
{
    /// <summary>Captures original bytes only when decoding exactly reproduces the compiler text.</summary>
    public static DocumentSnapshot Capture(DocumentSnapshot document, byte[] bytes, string encodingName, bool hasByteOrderMark)
    {
        var captured = document with
        {
            Fingerprint = Convert.ToHexString(SHA256.HashData(bytes)),
            EncodingName = encodingName,
            HasByteOrderMark = hasByteOrderMark,
            IsEditable = !document.IsGenerated,
        };
        if (!Encode(captured).AsSpan().SequenceEqual(bytes))
        {
            throw new InvalidDataException("Captured source text does not match original bytes.");
        }

        return captured;
    }

    /// <summary>Encodes captured source losslessly, preserving its BOM policy.</summary>
    public static byte[] Encode(DocumentSnapshot document)
    {
        var encoding = Encoding.GetEncoding(document.EncodingName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var content = encoding.GetBytes(document.Text);
        return document.HasByteOrderMark ? [.. encoding.GetPreamble(), .. content] : content;
    }

    internal static void Validate(DocumentSnapshot document)
    {
        if (document.IsEditable && (document.IsGenerated ||
            document.Fingerprint != Convert.ToHexString(SHA256.HashData(Encode(document)))))
        {
            throw new InvalidDataException("Editable source identity does not match captured text and bytes.");
        }
    }
}
