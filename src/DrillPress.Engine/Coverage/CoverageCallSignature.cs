using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;

namespace DrillPress.Engine;

internal sealed class CoverageCallSignature : ISignatureTypeProvider<string, object?>
{
    internal bool Matches(MetadataReader reader, int token, IMethodSymbol expected) =>
        MatchesDeclaration(reader, token, expected)
        || expected.OverriddenMethod is { } overridden && Matches(reader, token, overridden);

    internal static IMethodSymbol Slot(IMethodSymbol method)
    {
        while (method.OverriddenMethod is { } overridden)
            method = overridden;
        return method.OriginalDefinition;
    }

    private bool MatchesDeclaration(MetadataReader reader, int token, IMethodSymbol expected)
    {
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind == HandleKind.MethodSpecification)
        {
            var method = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
            var arguments = method.DecodeSignature(this, null);
            if (!arguments.SequenceEqual(expected.TypeArguments.Select(Type)))
                return false;
            handle = method.Method;
        }
        else if (expected.Arity != 0)
            return false;
        string name;
        EntityHandle owner;
        MethodSignature<string> signature;
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            name = reader.GetString(method.Name);
            owner = method.GetDeclaringType();
            signature = method.DecodeSignature(this, null);
        }
        else if (handle.Kind == HandleKind.MemberReference)
        {
            var method = reader.GetMemberReference((MemberReferenceHandle)handle);
            if (method.GetKind() != MemberReferenceKind.Method)
                return false;
            name = reader.GetString(method.Name);
            owner = method.Parent;
            signature = method.DecodeMethodSignature(this, null);
        }
        else
            return false;
        var definition = expected.OriginalDefinition;
        return name == definition.MetadataName
            && signature.Header.IsInstance != definition.IsStatic
            && signature.GenericParameterCount == definition.Arity
            && OwnerName(reader, owner) == CodeType.MetadataNameOf(definition.ContainingType)
            && AssemblyMatches(reader, owner, expected.ContainingAssembly.Identity)
            && signature.ReturnType
                == ParameterType(
                    definition.ReturnType,
                    definition.ReturnsByRef || definition.ReturnsByRefReadonly
                )
            && signature.ParameterTypes.SequenceEqual(
                definition.Parameters.Select(parameter =>
                    ParameterType(parameter.Type, parameter.RefKind != RefKind.None)
                )
            );
    }

    private string? OwnerName(MetadataReader reader, EntityHandle handle)
    {
        var value = handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(
                reader,
                (TypeDefinitionHandle)handle,
                0
            ),
            HandleKind.TypeReference => GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                0
            ),
            HandleKind.TypeSpecification => GetTypeFromSpecification(
                reader,
                null,
                (TypeSpecificationHandle)handle,
                0
            ),
            _ => null,
        };
        var generics = value?.IndexOf('[') ?? -1;
        return generics >= 0 ? value![..generics] : value;
    }

    private static string ParameterType(ITypeSymbol type, bool byRef) =>
        Type(type) + (byRef ? "&" : "");

    private static string Type(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol parameter => (
                parameter.TypeParameterKind == TypeParameterKind.Method ? "!!" : "!"
            ) + parameter.Ordinal,
            IArrayTypeSymbol array => Type(array.ElementType)
                + (array.IsSZArray ? "[]" : "[" + new string(',', array.Rank - 1) + "*]"),
            IPointerTypeSymbol pointer => Type(pointer.PointedAtType) + "*",
            INamedTypeSymbol named when named.IsGenericType => CodeType.MetadataNameOf(
                named.OriginalDefinition
            )
                + "["
                + string.Join(',', named.TypeArguments.Select(Type))
                + "]",
            INamedTypeSymbol named => CodeType.MetadataNameOf(named),
            _ => "unsupported-type",
        };

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

    public string GetArrayType(string elementType, ArrayShape shape) =>
        elementType + "[" + new string(',', shape.Rank - 1) + "*]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetFunctionPointerType(MethodSignature<string> signature) =>
        "unsupported-function-pointer";

    public string GetGenericInstantiation(
        string genericType,
        ImmutableArray<string> typeArguments
    ) => genericType + "[" + string.Join(',', typeArguments) + "]";

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
        isRequired ? "unsupported-modifier" : unmodifiedType;

    public string GetPinnedType(string elementType) => elementType;

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind
    )
    {
        var type = reader.GetTypeDefinition(handle);
        return type.GetDeclaringType().IsNil
            ? Qualified(reader.GetString(type.Namespace), reader.GetString(type.Name))
            : GetTypeFromDefinition(reader, type.GetDeclaringType(), rawTypeKind)
                + "+"
                + reader.GetString(type.Name);
    }

    public string GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind
    )
    {
        var type = reader.GetTypeReference(handle);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind)
                + "+"
                + reader.GetString(type.Name)
            : Qualified(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }

    public string GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind
    ) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    private static string Qualified(string ns, string name) =>
        ns.Length == 0 ? name : ns + "." + name;
}
