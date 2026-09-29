using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>An applied attribute's compiler constants; arbitrary constructors and property defaults are never executed.</summary>
public sealed class CodeAttribute(ISymbol owner, AttributeData data)
{
    /// <summary>Reads a compiler-recorded constructor value with exact parameter-name and type matching; absence and errors remain unavailable.</summary>
    public Optional<T> ConstructorValue<T>(string name) =>
        CompilerConstant.Read<T>(ConstructorArgument(name));

    /// <summary>Reads an explicitly supplied named property/field value without executing attribute code.</summary>
    public Optional<T> NamedValue<T>(string name) => CompilerConstant.Read<T>(NamedArgument(name));

    /// <summary>Reads a named Boolean argument, using the caller's explicit fallback when no typed value is available.</summary>
    public bool FlagOrDefault(string name, bool fallback) =>
        NamedValue<bool>(name) is { HasValue: true } value ? value.Value : fallback;

    /// <summary>The declaration on which this attribute is applied, not a runtime descendant.</summary>
    public ISymbol Owner { get; } = owner;

    /// <summary>The compiler evidence, including the actual concrete attribute type and source syntax when present.</summary>
    public AttributeData Data { get; } = data;

    /// <summary>Reads an explicitly supplied named value. HasValue distinguishes absence from explicit null/false or an erroneous typed constant.</summary>
    public Optional<TypedConstant> NamedArgument(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var pair in Data.NamedArguments)
            if (pair.Key == name)
                return new(pair.Value);
        return default;
    }

    /// <summary>Reads a constructor argument by its bound parameter name, including compiler-supplied optional defaults.</summary>
    public Optional<TypedConstant> ConstructorArgument(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        var parameter = Data.AttributeConstructor?.Parameters.FirstOrDefault(p =>
            p.Name == parameterName
        );
        return parameter is not null && parameter.Ordinal < Data.ConstructorArguments.Length
            ? new(Data.ConstructorArguments[parameter.Ordinal])
            : default;
    }

    /// <summary>Matches the configured marker or its base chain; qualification applies to the marker, not every derived attribute.</summary>
    public bool Matches(CodeType marker, bool includeDerived = true) =>
        Data.AttributeClass is { TypeKind: not TypeKind.Error } type
        && (includeDerived ? Symbols.IsOrDerivesFrom(type, marker) : marker.Matches(type));
}
