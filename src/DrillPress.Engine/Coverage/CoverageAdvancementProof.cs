using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;

namespace DrillPress.Engine;

internal static class CoverageAdvancementProof
{
    internal static bool Matches(
        MetadataReader metadata,
        MethodBodyBlock body,
        int offset,
        int end,
        MethodDefinition enclosing,
        IMethodSymbol expected
    )
    {
        if (expected.Parameters.Length != 0 || expected.Arity != 0)
            return false;
        var reader = body.GetILReader();
        if (offset < 0 || end > reader.Length || offset >= end)
            return false;
        reader.Offset = offset;
        var loadedThis = false;
        while (reader.Offset < end)
        {
            var opcode = (ILOpCode)reader.ReadByte();
            if ((byte)opcode == 0xfe)
                opcode = (ILOpCode)(0xfe00 | reader.ReadByte());
            switch (opcode)
            {
                case ILOpCode.Nop:
                case ILOpCode.Ldloc_0:
                case ILOpCode.Ldloc_1:
                case ILOpCode.Ldloc_2:
                case ILOpCode.Ldloc_3:
                case ILOpCode.Readonly:
                    loadedThis = false;
                    break;
                case ILOpCode.Ldloc_s:
                case ILOpCode.Ldloca_s:
                    reader.ReadByte();
                    loadedThis = false;
                    break;
                case ILOpCode.Ldloc:
                case ILOpCode.Ldloca:
                    reader.ReadUInt16();
                    loadedThis = false;
                    break;
                case ILOpCode.Ldarg_0 when !enclosing.Attributes.HasFlag(MethodAttributes.Static):
                    loadedThis = true;
                    break;
                case ILOpCode.Ldfld when loadedThis:
                    reader.ReadInt32();
                    loadedThis = false;
                    break;
                case ILOpCode.Constrained:
                    reader.ReadInt32();
                    loadedThis = false;
                    break;
                case ILOpCode.Call:
                case ILOpCode.Callvirt:
                    var handle = MetadataTokens.EntityHandle(reader.ReadInt32());
                    return reader.Offset <= end && MethodMatches(metadata, handle, expected);
                default:
                    return false;
            }
            if (reader.Offset > end)
                return false;
        }
        return false;
    }

    private static bool MethodMatches(
        MetadataReader reader,
        EntityHandle handle,
        IMethodSymbol expected
    )
    {
        string name;
        EntityHandle owner;
        BlobHandle signature;
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            name = reader.GetString(method.Name);
            owner = method.GetDeclaringType();
            signature = method.Signature;
        }
        else if (handle.Kind == HandleKind.MemberReference)
        {
            var method = reader.GetMemberReference((MemberReferenceHandle)handle);
            name = reader.GetString(method.Name);
            owner = method.Parent;
            signature = method.Signature;
        }
        else
            return false;
        var blob = reader.GetBlobReader(signature);
        var header = blob.ReadSignatureHeader();
        return name == expected.MetadataName
            && header.Kind == SignatureKind.Method
            && !header.IsGeneric
            && blob.ReadCompressedInteger() == 0
            && TypeName(reader, owner)
                == CodeType.MetadataNameOf(expected.ContainingType.OriginalDefinition)
            && AssemblyMatches(reader, owner, expected.ContainingAssembly.Identity);
    }

    private static bool AssemblyMatches(
        MetadataReader reader,
        EntityHandle owner,
        AssemblyIdentity expected
    )
    {
        if (owner.Kind == HandleKind.TypeSpecification)
        {
            var blob = reader.GetBlobReader(
                reader.GetTypeSpecification((TypeSpecificationHandle)owner).Signature
            );
            var code = blob.ReadByte();
            if (code == 0x15)
                code = blob.ReadByte();
            return code is 0x11 or 0x12 && AssemblyMatches(reader, blob.ReadTypeHandle(), expected);
        }
        if (owner.Kind == HandleKind.TypeReference)
        {
            var scope = reader.GetTypeReference((TypeReferenceHandle)owner).ResolutionScope;
            if (scope.Kind == HandleKind.TypeReference)
                return AssemblyMatches(reader, scope, expected);
            if (scope.Kind != HandleKind.AssemblyReference)
                return false;
            var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)scope);
            return IdentityMatches(
                reader,
                assembly.Name,
                assembly.Version,
                assembly.Culture,
                assembly.PublicKeyOrToken,
                assembly.Flags.HasFlag(AssemblyFlags.PublicKey),
                expected
            );
        }
        if (owner.Kind != HandleKind.TypeDefinition || !reader.IsAssembly)
            return false;
        var definition = reader.GetAssemblyDefinition();
        return IdentityMatches(
            reader,
            definition.Name,
            definition.Version,
            definition.Culture,
            definition.PublicKey,
            true,
            expected
        );
    }

    private static bool IdentityMatches(
        MetadataReader reader,
        StringHandle name,
        Version version,
        StringHandle culture,
        BlobHandle key,
        bool fullKey,
        AssemblyIdentity expected
    )
    {
        var token = reader.GetBlobBytes(key);
        if (fullKey && token.Length > 0)
            token = SHA1.HashData(token)[^8..].Reverse().ToArray();
        return reader.GetString(name) == expected.Name
            && version == expected.Version
            && reader.GetString(culture) == expected.CultureName
            && token.AsSpan().SequenceEqual(expected.PublicKeyToken.AsSpan());
    }

    private static string? TypeName(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeSpecification)
        {
            var blob = reader.GetBlobReader(
                reader.GetTypeSpecification((TypeSpecificationHandle)handle).Signature
            );
            var code = blob.ReadByte();
            if (code == 0x15)
                code = blob.ReadByte();
            if (code is not (0x11 or 0x12))
                return null;
            return TypeName(reader, blob.ReadTypeHandle());
        }
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var type = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            return type.GetDeclaringType().IsNil
                ? Qualified(reader.GetString(type.Namespace), reader.GetString(type.Name))
                : TypeName(reader, type.GetDeclaringType()) + "+" + reader.GetString(type.Name);
        }
        if (handle.Kind == HandleKind.TypeReference)
        {
            var type = reader.GetTypeReference((TypeReferenceHandle)handle);
            return type.ResolutionScope.Kind == HandleKind.TypeReference
                ? TypeName(reader, type.ResolutionScope) + "+" + reader.GetString(type.Name)
                : Qualified(reader.GetString(type.Namespace), reader.GetString(type.Name));
        }
        return null;
    }

    private static string Qualified(string ns, string name) =>
        ns.Length == 0 ? name : ns + "." + name;
}
