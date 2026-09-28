using Microsoft.CodeAnalysis;

namespace DrillPress.Semantics;

/// <summary>Additional overload constraints. Unspecified components remain wildcards; matching never asserts behavioral equivalence.</summary>
public sealed class MethodSignature
{
    private readonly CodeType[]? _typeArguments;
    private readonly RefKind[]? _parameterRefKinds;

    /// <summary>Captures optional signature constraints without retaining mutable caller collections.</summary>
    public MethodSignature(
        int? genericArity = null,
        bool? isStatic = null,
        CodeType? returnType = null,
        RefKind? returnRefKind = null,
        IReadOnlyList<CodeType>? typeArguments = null,
        IReadOnlyList<RefKind>? parameterRefKinds = null
    )
    {
        if (genericArity < 0)
            throw new ArgumentOutOfRangeException(nameof(genericArity));
        GenericArity = genericArity;
        IsStatic = isStatic;
        ReturnType = returnType;
        ReturnRefKind = returnRefKind;
        _typeArguments = typeArguments?.ToArray();
        _parameterRefKinds = parameterRefKinds?.ToArray();
    }

    /// <summary>Required generic method arity, or no restriction.</summary>
    public int? GenericArity { get; }

    /// <summary>Required static declaration shape; extension declarations remain static.</summary>
    public bool? IsStatic { get; }

    /// <summary>Required constructed return type, or no restriction.</summary>
    public CodeType? ReturnType { get; }

    /// <summary>Required return passing mode, or no restriction.</summary>
    public RefKind? ReturnRefKind { get; }

    /// <summary>Matches the selected constructed method against every specified constraint.</summary>
    public bool Matches(IMethodSymbol method) =>
        (GenericArity is null || method.Arity == GenericArity)
        && (IsStatic is null || method.IsStatic == IsStatic)
        && (ReturnType is null || ReturnType.Value.Matches(method.ReturnType))
        && (ReturnRefKind is null || method.RefKind == ReturnRefKind)
        && (
            _typeArguments is null
            || method.TypeArguments.Length == _typeArguments.Length
                && method
                    .TypeArguments.Zip(_typeArguments)
                    .All(pair => pair.Second.Matches(pair.First))
        )
        && (
            _parameterRefKinds is null
            || method.Parameters.Select(p => p.RefKind).SequenceEqual(_parameterRefKinds)
        );
}
