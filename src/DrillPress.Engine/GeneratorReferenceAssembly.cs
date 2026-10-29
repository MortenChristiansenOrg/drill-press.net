using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace DrillPress.Engine;

internal sealed class GeneratorReferenceAssembly
{
    private readonly Lazy<(PortableExecutableReference Reference, byte[] Bytes)?> _assembly;

    private readonly Lazy<MetadataReferenceSnapshot?> _image;

    internal GeneratorReferenceAssembly(
        CSharpCompilation compilation,
        string contextId,
        GeneratorTransport transport,
        CancellationToken cancellationToken
    )
    {
        _assembly = new(() =>
        {
            using var bytes = new MemoryStream();
            var result = compilation.Emit(
                bytes,
                options: new EmitOptions(
                    debugInformationFormat: DebugInformationFormat.PortablePdb,
                    metadataOnly: true,
                    includePrivateMembers: false
                ),
                cancellationToken: cancellationToken
            );
            if (!result.Success)
                return null;
            var contents = bytes.ToArray();
            return (MetadataReference.CreateFromImage(contents), contents);
        });
        _image = new(() =>
            _assembly.Value is { } assembly
                ? transport.WriteReference(contextId, assembly.Bytes)
                : null
        );
    }

    internal PortableExecutableReference? Reference => _assembly.Value?.Reference;

    internal MetadataReferenceSnapshot? Image => _image.Value;
}
