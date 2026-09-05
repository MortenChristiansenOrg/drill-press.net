namespace DrillPress;

/// <summary>
/// Identifies a CLR type by its namespace-qualified metadata name so compiled rules can
/// match Roslyn symbols without referencing the analyzed project.
/// </summary>
/// <param name="MetadataName">The namespace-qualified metadata name.</param>
public readonly record struct CodeType(string MetadataName)
{
    /// <summary>Creates an identity from a compile-time checked runtime type.</summary>
    public static CodeType Of<T>() => new(GetMetadataName(typeof(T)));

    /// <summary>Creates an identity for a type that the rule project cannot reference directly.</summary>
    /// <param name="metadataName">The namespace-qualified metadata name.</param>
    public static CodeType Named(string metadataName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataName);
        return new CodeType(metadataName);
    }

    private static string GetMetadataName(Type type)
    {
        if (type.IsConstructedGenericType)
        {
            type = type.GetGenericTypeDefinition();
        }

        if (type.DeclaringType is not null)
        {
            return $"{GetMetadataName(type.DeclaringType)}+{type.Name}";
        }

        return string.IsNullOrEmpty(type.Namespace)
            ? type.Name
            : $"{type.Namespace}.{type.Name}";
    }
}
