using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DrillPress.UnitTests.TestInfrastructure;

internal static class MetadataOnlyAssembly
{
    internal static byte[] Create(string name, Version version)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(name + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default
        );
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            version,
            default,
            default,
            (AssemblyFlags)0,
            AssemblyHashAlgorithm.Sha256
        );
        var image = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll
            ),
            new MetadataRootBuilder(metadata),
            new BlobBuilder()
        );
        var bytes = new BlobBuilder();
        image.Serialize(bytes);
        return bytes.ToArray();
    }
}
