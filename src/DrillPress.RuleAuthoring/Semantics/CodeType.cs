using Microsoft.CodeAnalysis;

namespace DrillPress.Semantics;

/// <summary>A metadata identity with optional assembly qualification and exact constructed arguments.</summary>
/// <param name="MetadataName">Namespace-qualified metadata name, using + for nested types.</param>
public readonly record struct CodeType(string MetadataName)
{
    private bool AllowFrameworkFacades { get; init; }

    /// <summary>Optional assembly simple name or full display identity; null permits any declaring assembly.</summary>
    public string? AssemblyName { get; init; }

    /// <summary>Canonical constructed argument identity; empty selects the generic definition.</summary>
    public string TypeArguments { get; init; } = "";

    /// <summary>Captures a statically referenced type, including its constructed generic arguments.</summary>
    public static CodeType Of<T>() => FromRuntime(typeof(T));

    /// <summary>Names a target type without referencing its assembly from the rule project.</summary>
    public static CodeType Named(string metadataName, string? assemblyName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataName);
        if (assemblyName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        }

        return new(metadataName) { AssemblyName = assemblyName };
    }

    /// <summary>Matches semantic identity, allowing the standard framework reference-assembly facades.</summary>
    public bool Matches(INamedTypeSymbol symbol)
    {
        if (symbol.TypeKind == TypeKind.Error)
        {
            return false;
        }

        var actual = FromSymbol(symbol);
        return MetadataName == actual.MetadataName &&
            (TypeArguments.Length == 0 || TypeArguments == actual.TypeArguments) &&
            (AssemblyName is null || AssemblyName == actual.AssemblyName || AssemblyName == symbol.ContainingAssembly.Identity.ToString() ||
                AllowFrameworkFacades && IsFrameworkSymbol(symbol));
    }

    /// <summary>Matches named types and arrays, preserving array rank, element identity and constructed generic arguments.</summary>
    public bool Matches(ITypeSymbol symbol)
    {
        if (symbol is INamedTypeSymbol named)
        {
            return Matches(named);
        }

        if (symbol is IArrayTypeSymbol array)
        {
            var suffix = "[" + new string(',', array.Rank - 1) + "]";
            return MetadataName.EndsWith(suffix, StringComparison.Ordinal) &&
                (this with { MetadataName = MetadataName[..^suffix.Length] }).Matches(array.ElementType);
        }

        return false;
    }

    internal static CodeType FromSymbol(INamedTypeSymbol symbol) => new(MetadataNameOf(symbol))
    {
        AssemblyName = symbol.ContainingAssembly.Identity.Name,
        TypeArguments = symbol.IsGenericType && !SymbolEqualityComparer.Default.Equals(symbol, symbol.OriginalDefinition)
            ? string.Join(",", AllTypeArguments(symbol).Select(SymbolArgument)) : "",
    };

    internal static string MetadataNameOf(INamedTypeSymbol symbol) => symbol.ContainingType is { } parent
        ? $"{MetadataNameOf(parent)}+{symbol.MetadataName}"
        : symbol.ContainingNamespace.IsGlobalNamespace ? symbol.MetadataName
        : $"{symbol.ContainingNamespace.ToDisplayString()}.{symbol.MetadataName}";

    internal static bool IsFrameworkSymbol(INamedTypeSymbol symbol)
    {
        var token = Convert.ToHexString(symbol.ContainingAssembly.Identity.PublicKeyToken.AsSpan());
        return IsFrameworkAssembly(symbol.ContainingAssembly.Name) && token is
            "B03F5F7F11D50A3A" or "B77A5C561934E089" or "7CEC85D7BEA7798E";
    }

    private static bool IsFrameworkAssembly(string name) => name is "mscorlib" or "netstandard" ||
        name.StartsWith("System.", StringComparison.Ordinal) || name == "System";

    private static string SymbolArgument(ITypeSymbol symbol) => symbol switch
    {
        INamedTypeSymbol named => MetadataNameOf(named) + "@" + (IsFrameworkSymbol(named) ? "framework" : named.ContainingAssembly.Identity.Name) + (named.IsGenericType
            ? "[" + string.Join(",", AllTypeArguments(named).Select(SymbolArgument)) + "]" : ""),
        IArrayTypeSymbol array => SymbolArgument(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]",
        _ => symbol.ToDisplayString(),
    };

    private static CodeType FromRuntime(Type type) => type.IsArray
        ? FromRuntime(type.GetElementType()!) with { MetadataName = RuntimeName(type) }
        : new(RuntimeName(type))
    {
        AssemblyName = type.Assembly.GetName().Name,
        AllowFrameworkFacades = RuntimeAssembly(type) == "framework",
        TypeArguments = type.IsConstructedGenericType ? string.Join(",", type.GenericTypeArguments.Select(RuntimeArgument)) : "",
    };

    private static string RuntimeArgument(Type type) => type.IsArray
        ? RuntimeArgument(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]"
        : RuntimeName(type) + "@" + RuntimeAssembly(type) + (type.IsConstructedGenericType
            ? "[" + string.Join(",", type.GenericTypeArguments.Select(RuntimeArgument)) + "]" : "");

    private static IEnumerable<ITypeSymbol> AllTypeArguments(INamedTypeSymbol type) =>
        (type.ContainingType is { } parent ? AllTypeArguments(parent) : []).Concat(type.TypeArguments);

    private static string RuntimeAssembly(Type type)
    {
        var assembly = type.Assembly.GetName();
        var token = Convert.ToHexString(assembly.GetPublicKeyToken() ?? []);
        return assembly.Name is { } name && IsFrameworkAssembly(name) && token is
            "B03F5F7F11D50A3A" or "B77A5C561934E089" or "7CEC85D7BEA7798E" ? "framework" : assembly.Name ?? "";
    }

    private static string RuntimeName(Type type)
    {
        if (type.IsArray)
        {
            return RuntimeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        var definition = type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type;
        return definition.FullName ?? definition.Name;
    }
}
