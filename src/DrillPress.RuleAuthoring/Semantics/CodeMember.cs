using Microsoft.CodeAnalysis;

namespace DrillPress.Semantics;

/// <summary>A configured member identity. Omitted parameter types match fields, properties and every method overload; an empty list matches only parameterless methods.</summary>
public sealed class CodeMember
{
    private readonly CodeType[]? _parameters;
    private readonly Lazy<CodeQuery<MemberReference>> _references;

    /// <summary>Names a member on its declaring type, optionally selecting exact method parameter types.</summary>
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
        _references = new(() => Code.MemberReferences.Where(CreateReferenceCondition()));
    }

    /// <summary>The semantic declaring type, optionally qualified by assembly.</summary>
    public CodeType DeclaringType { get; }

    /// <summary>The compiler member name, including .ctor for constructors.</summary>
    public string Name { get; }

    /// <summary>Selects ordinary source references to this member, preserving name-based query optimization.</summary>
    /// <remarks>Includes named field, property and method expressions, not object creation, indexer syntax or implicit uses.</remarks>
    public CodeQuery<MemberReference> References => _references.Value;

    /// <summary>Creates an exact method-overload identity without changing this member. No arguments selects the parameterless overload.</summary>
    public CodeMember WithParameters(params CodeType[] parameters) =>
        new(DeclaringType, Name, parameters);

    private RuleCondition<MemberReference> CreateReferenceCondition()
    {
        var family = Members.Are(DeclaringType, Name);
        return new(
            reference =>
                reference.Symbol is { } symbol
                    ? Matches(symbol)
                    : _parameters is null && family.Evaluate(reference),
            new HashSet<string> { Name }
        );
    }

    /// <summary>Matches a field, property or method on the configured declaring type. Parameter-constrained identities match methods only.</summary>
    public bool Matches(ISymbol symbol) =>
        symbol switch
        {
            IMethodSymbol method => Matches(method),
            IFieldSymbol or IPropertySymbol => _parameters is null
                && symbol.Name == Name
                && symbol.ContainingType is { } type
                && DeclaringType.Matches(type),
            _ => false,
        };

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
