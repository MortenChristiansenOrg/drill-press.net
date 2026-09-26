using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DrillPress.Release;

public class AssemblyVersionReader
{
    public virtual string Read(Stream stream)
    {
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference)
            {
                continue;
            }

            var member = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (
                metadata.GetString(type.Namespace) == "System.Reflection"
                && metadata.GetString(type.Name) == "AssemblyInformationalVersionAttribute"
            )
            {
                var value = metadata.GetBlobReader(attribute.Value);
                value.ReadUInt16();
                return value.ReadSerializedString() ?? "";
            }
        }

        throw new InvalidDataException("Assembly has no informational version.");
    }
}
