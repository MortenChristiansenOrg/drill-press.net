using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>A value code can use by name at a position: a local, a parameter, or a field or property of the containing types.</summary>
/// <param name="Symbol">The local, parameter, field or property.</param>
/// <param name="Kind">Which of these it is.</param>
/// <param name="Type">Its declared type, including nullable annotations.</param>
public sealed record ScopedValue(ISymbol Symbol, ScopedValueKind Kind, ITypeSymbol Type)
{
    /// <summary>The name code uses to read the value.</summary>
    public string Name => Symbol.Name;
}

/// <summary>Where a value in scope is declared.</summary>
public enum ScopedValueKind
{
    /// <summary>A local variable declared before the position, in the function or an enclosing one.</summary>
    Local,

    /// <summary>A parameter of the function, an enclosing function, or the type's primary constructor.</summary>
    Parameter,

    /// <summary>A field of the containing types.</summary>
    Field,

    /// <summary>A property of the containing types, excluding indexers.</summary>
    Property,
}
