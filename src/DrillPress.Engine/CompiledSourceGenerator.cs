using DrillPress.Manifest;
using Microsoft.CodeAnalysis;

namespace DrillPress.Engine;

/// <summary>A statically linked source generator paired with the SHA-256 identity of its original analyzer assembly.</summary>
/// <remarks>The fingerprint prevents a native bundle from silently substituting another generator version.</remarks>
public sealed class CompiledSourceGenerator
{
    /// <summary>Registers the actual generator implementation and the fingerprint recorded when the bundle was built.</summary>
    public CompiledSourceGenerator(ISourceGenerator generator, string assemblyFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyFingerprint);
        if (assemblyFingerprint.Length != 64 || !assemblyFingerprint.All(char.IsAsciiHexDigit))
            throw new ArgumentException(
                "Expected a SHA-256 assembly fingerprint.",
                nameof(assemblyFingerprint)
            );
        Generator = generator;
        var type = generator.GetGeneratorType();
        Description = new GeneratorDescription(
            type.FullName!,
            type.Assembly.FullName!,
            assemblyFingerprint.ToUpperInvariant()
        );
    }

    internal ISourceGenerator Generator { get; }

    internal GeneratorDescription Description { get; }
}
