using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>The written declaration a property view is anchored to.</summary>
public enum PropertyViewOrigin
{
    /// <summary>An explicit property declaration, including one that redeclares a positional record member.</summary>
    PropertyDeclaration,

    /// <summary>A positional record parameter from which the compiler synthesized the property.</summary>
    PositionalParameter,

    /// <summary>An explicit indexer declaration.</summary>
    Indexer,
}

/// <summary>The kind of written default value. A parameter default applies to the primary constructor and is not a property initializer or a serializer default.</summary>
public enum PropertyDefaultKind
{
    /// <summary>The value of a property initializer.</summary>
    PropertyInitializer,

    /// <summary>The default value of a positional record parameter.</summary>
    PositionalParameterDefault,
}

/// <summary>The nullability declared by a property, after generic substitution. These are declaration facts, not expression flow state.</summary>
public enum PropertyNullability
{
    /// <summary>The property type did not bind.</summary>
    Unavailable,

    /// <summary>A reference type declared where nullable annotations are disabled; the rule decides how to treat it.</summary>
    Oblivious,

    /// <summary>A non-nullable reference declaration or a non-nullable value type.</summary>
    NonNullable,

    /// <summary>An annotated reference declaration or <c>Nullable&lt;T&gt;</c>.</summary>
    Nullable,

    /// <summary>An open type parameter whose constraints leave nullability to the type argument.</summary>
    TypeParameter,
}

/// <summary>A written default value, keeping the kind of declaration that supplied it.</summary>
/// <param name="Kind">Whether a property initializer or a positional parameter default supplied the value.</param>
/// <param name="Value">The written value; <see cref="CodeExpression.ResultValues"/> reads its evidence.</param>
public sealed record PropertyDefault(PropertyDefaultKind Kind, CodeExpression Value);

/// <summary>Selects properties for a property view projection. The defaults match <see cref="TypeTraversal.ThroughProperties"/>: public getters on inherited, instance, non-indexer properties in ordinary source.</summary>
public sealed record PropertyViewOptions
{
    /// <summary>An additional property filter, applied after hidden and overridden members are removed.</summary>
    public Func<IPropertySymbol, bool>? Where { get; init; }

    /// <summary>Limits reporting to source in matching projects; by default every compatible project qualifies.</summary>
    public Func<AnalysisProject, bool>? Owner { get; init; }

    /// <summary>Whether to include properties declared by base types and base interfaces.</summary>
    public bool Inherited { get; init; } = true;

    /// <summary>Whether to include static properties.</summary>
    public bool IncludeStatic { get; init; }

    /// <summary>Whether to include indexers.</summary>
    public bool IncludeIndexers { get; init; }

    /// <summary>Whether to include properties without a public getter, including write-only properties.</summary>
    public bool IncludeNonPublic { get; init; }

    /// <summary>Whether a property declared only in generated source can produce a view. Metadata properties have no source and never do.</summary>
    public bool IncludeGenerated { get; init; }
}

internal sealed record PropertyViewInput(
    ITypeSymbol Type,
    AnalysisProject Context,
    TypeReachability? Reachability
);
