using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>
/// A stable CLR type identity used to match a runtime type from the rule bundle
/// against a Roslyn type in the target compilation.
/// </summary>
public readonly record struct CodeType
{
    private readonly ImmutableArray<CodeType> _typeArguments;

    private CodeType(
        string metadataName,
        string? assemblyName,
        ImmutableArray<CodeType> typeArguments = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataName);
        MetadataName = metadataName;
        AssemblyName = assemblyName;
        _typeArguments = typeArguments.IsDefault ? [] : typeArguments;
    }

    /// <summary>The namespace-qualified metadata name, including generic arity.</summary>
    public string MetadataName { get; }

    /// <summary>
    /// Optional simple assembly name. Generic references include it; named
    /// references may omit it when the rules project cannot reference the type.
    /// </summary>
    public string? AssemblyName { get; }

    /// <summary>
    /// Type arguments to match exactly. An empty collection means the type is
    /// non-generic or that a named generic identity matches any construction.
    /// </summary>
    public IReadOnlyList<CodeType> TypeArguments => Arguments;

    internal ImmutableArray<CodeType> Arguments => _typeArguments.IsDefault ? [] : _typeArguments;

    public static CodeType Of<T>() => CodeTypeCache<T>.Value;

    public static CodeType Named(string metadataName, string? assemblyName = null) =>
        new(metadataName, assemblyName);

    public CodeType ConstructedWith(params CodeType[] typeArguments)
    {
        ArgumentNullException.ThrowIfNull(typeArguments);
        return new CodeType(MetadataName, AssemblyName, [.. typeArguments]);
    }

    public override string ToString()
    {
        var arguments = Arguments;
        var typeArguments = arguments.IsEmpty
            ? string.Empty
            : $"<{string.Join(", ", arguments)}>";
        var identity = $"{MetadataName}{typeArguments}";
        return AssemblyName is null ? identity : $"{identity}, {AssemblyName}";
    }

    private static CodeType FromRuntimeType(Type runtimeType)
    {
        var typeArguments = runtimeType.IsConstructedGenericType
            ? runtimeType.GetGenericArguments().Select(FromRuntimeType).ToImmutableArray()
            : ImmutableArray<CodeType>.Empty;
        var typeDefinition = runtimeType.IsConstructedGenericType
            ? runtimeType.GetGenericTypeDefinition()
            : runtimeType;

        // Core-library types are exposed through reference-assembly facades to
        // Roslyn, so their runtime implementation assembly is not a stable part
        // of source-level identity. Metadata name remains stable across both.
        var assemblyName = typeDefinition.Assembly == typeof(object).Assembly
            ? null
            : typeDefinition.Assembly.GetName().Name
                ?? throw new InvalidOperationException($"Type '{runtimeType}' has no assembly name.");
        return new CodeType(GetMetadataName(typeDefinition), assemblyName, typeArguments);
    }

    private static string GetMetadataName(Type runtimeType)
    {
        if (runtimeType.IsArray)
        {
            var elementType = runtimeType.GetElementType()
                ?? throw new InvalidOperationException($"Array type '{runtimeType}' has no element type.");
            var commas = runtimeType.GetArrayRank() == 1 ? string.Empty : new string(',', runtimeType.GetArrayRank() - 1);
            return $"{GetMetadataName(elementType)}[{commas}]";
        }

        if (runtimeType.IsPointer)
        {
            return $"{GetMetadataName(runtimeType.GetElementType()!)}*";
        }

        if (runtimeType.IsByRef)
        {
            return $"{GetMetadataName(runtimeType.GetElementType()!)}&";
        }

        if (runtimeType.DeclaringType is not null)
        {
            return $"{GetMetadataName(runtimeType.DeclaringType)}+{runtimeType.Name}";
        }

        return string.IsNullOrEmpty(runtimeType.Namespace)
            ? runtimeType.Name
            : $"{runtimeType.Namespace}.{runtimeType.Name}";
    }

    private static class CodeTypeCache<T>
    {
        internal static readonly CodeType Value = FromRuntimeType(typeof(T));
    }
}

internal static class CodeTypeMatching
{
    public static bool Matches(ITypeSymbol? symbol, CodeType expected)
    {
        if (symbol is null)
        {
            return false;
        }

        var actualName = GetMetadataName(symbol);
        if (!StringComparer.Ordinal.Equals(actualName, expected.MetadataName))
        {
            return false;
        }

        if (expected.AssemblyName is not null &&
            !StringComparer.Ordinal.Equals(GetAssemblyName(symbol), expected.AssemblyName))
        {
            return false;
        }

        if (expected.Arguments.IsEmpty)
        {
            return true;
        }

        return symbol is INamedTypeSymbol named &&
               named.TypeArguments.Length == expected.Arguments.Length &&
               named.TypeArguments.Zip(expected.Arguments).All(pair => Matches(pair.First, pair.Second));
    }

    private static string GetMetadataName(ITypeSymbol symbol) => symbol switch
    {
        IArrayTypeSymbol array =>
            $"{GetMetadataName(array.ElementType)}[{new string(',', array.Rank - 1)}]",
        IPointerTypeSymbol pointer => $"{GetMetadataName(pointer.PointedAtType)}*",
        INamedTypeSymbol named => GetNamedMetadataName(named.OriginalDefinition),
        _ => SymbolNames.FullName(symbol as INamedTypeSymbol),
    };

    private static string GetNamedMetadataName(INamedTypeSymbol type)
    {
        if (type.ContainingType is not null)
        {
            return $"{GetNamedMetadataName(type.ContainingType)}+{type.MetadataName}";
        }

        return type.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
            ? $"{containingNamespace.ToDisplayString()}.{type.MetadataName}"
            : type.MetadataName;
    }

    private static string? GetAssemblyName(ITypeSymbol symbol) => symbol switch
    {
        IArrayTypeSymbol array => GetAssemblyName(array.ElementType),
        IPointerTypeSymbol pointer => GetAssemblyName(pointer.PointedAtType),
        _ => symbol.ContainingAssembly?.Identity.Name,
    };
}
