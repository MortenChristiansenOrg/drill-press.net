using System.Text;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class SourceIdentityTests
{
    [Theory]
    [InlineData("utf-8", false)]
    [InlineData("utf-8", true)]
    [InlineData("utf-16", true)]
    [InlineData("utf-16BE", true)]
    public void Encoding_BOM_and_surrogate_pairs_round_trip_losslessly(
        string encodingName,
        bool bom
    )
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var preamble = encoding
            .GetPreamble()
            .Take(encoding.GetPreamble().Length * Convert.ToInt32(bom));
        byte[] bytes = [.. preamble, .. encoding.GetBytes("😀æ\r\nnext\nlast\r")];
        var source = new DocumentSnapshot("Source.cs", "😀æ\r\nnext\nlast\r", false);

        var captured = SourceIdentity.Capture(source, bytes, encodingName, bom);

        Assert.Equal(bytes, SourceIdentity.Encode(captured));
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            captured.Fingerprint
        );
        Assert.True(captured.IsEditable);
    }

    [Fact]
    public void Mismatched_original_bytes_cannot_be_marked_editable()
    {
        var source = new DocumentSnapshot("Source.cs", "captured", false);

        Assert.Throws<InvalidDataException>(() =>
            SourceIdentity.Capture(source, Encoding.UTF8.GetBytes("changed"), "utf-8", false)
        );
    }

    [Fact]
    public void Unrepresentable_replacement_fails_losslessly()
    {
        var source = new DocumentSnapshot("Source.cs", "😀", false) { EncodingName = "us-ascii" };

        Assert.Throws<EncoderFallbackException>(() => SourceIdentity.Encode(source));
    }

    [Fact]
    public void Unsupported_encodings_have_a_consistent_validation_error()
    {
        var source = new DocumentSnapshot("Source.cs", "text", false)
        {
            EncodingName = "unknown-encoding",
        };

        var exception = Assert.Throws<InvalidDataException>(() => SourceIdentity.Encode(source));

        Assert.Equal("Source encoding 'unknown-encoding' is not supported.", exception.Message);
    }
}
