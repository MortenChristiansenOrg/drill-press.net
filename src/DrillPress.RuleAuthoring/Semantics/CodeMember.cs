using Microsoft.CodeAnalysis;

namespace DrillPress.Semantics;

/// <summary>A configured method identity. Omitted parameter types match every overload; an empty list matches only parameterless methods.</summary>
public sealed class CodeMember
{
    private readonly CodeType[]? _parameters;

    /// <summary>Names a method on its declaring type, optionally selecting exact parameter types.</summary>
    public CodeMember(
        CodeType declaringType,
        string name,
        IReadOnlyList<CodeType>? parameters = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        DeclaringType = declaringType;
        Name = name;
        _parameters = parameters?.ToArray();
    }

    /// <summary>The semantic declaring type, optionally qualified by assembly.</summary>
    public CodeType DeclaringType { get; }

    /// <summary>The compiler method name, including .ctor for constructors.</summary>
    public string Name { get; }

    /// <summary>Matches the original declaration of extension and constructed generic methods.</summary>
    public bool Matches(IMethodSymbol method)
    {
        method = method.ReducedFrom ?? method;
        return method.Name == Name
            && DeclaringType.Matches(method.ContainingType)
            && (
                _parameters is null
                || method.Parameters.Length == _parameters.Length
                    && method
                        .Parameters.Zip(_parameters)
                        .All(pair => pair.Second.Matches(pair.First.Type))
            );
    }
}
