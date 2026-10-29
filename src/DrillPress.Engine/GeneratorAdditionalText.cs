using System.Text;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

internal sealed class GeneratorAdditionalText(GeneratorAdditionalInput input) : AdditionalText
{
    private readonly Lazy<SourceText?> _text = new(() =>
        input.Contents is null
            ? null
            : SourceText.From(
                new MemoryStream(input.Contents),
                input.EncodingName is null ? null : Encoding.GetEncoding(input.EncodingName),
                (SourceHashAlgorithm)input.ChecksumAlgorithm
            )
    );

    public override string Path => input.Path;

    public override SourceText? GetText(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _text.Value;
    }
}
