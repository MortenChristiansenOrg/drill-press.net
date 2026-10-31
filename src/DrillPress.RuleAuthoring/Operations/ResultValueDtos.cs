namespace DrillPress;

/// <summary>What the language itself establishes about one expression that can supply a result. Nullable suppression never changes the kind.</summary>
public enum ResultValueKind
{
    /// <summary>A null literal, constant null, or empty <c>Nullable&lt;T&gt;</c> such as <c>default(int?)</c> or <c>new int?()</c>.</summary>
    Null,

    /// <summary>The default of a reference type, which is null.</summary>
    ReferenceDefault,

    /// <summary>A non-null compile-time constant.</summary>
    Constant,

    /// <summary>A value the language always creates: object, array, collection, anonymous or delegate creation, a <c>with</c> copy, interpolated text or <c>typeof</c>.</summary>
    Construction,

    /// <summary>The default of a non-nullable value type, such as <c>default(Guid)</c>.</summary>
    ValueTypeDefault,

    /// <summary>Any other expression whose static type is a non-nullable value type.</summary>
    ValueTypeValue,

    /// <summary>The default of a type parameter that may be a value or a reference type.</summary>
    TypeParameterDefault,

    /// <summary>No language-level fact, such as a call or member read. Read the expression's <c>DeclaredNullability</c> and, unless suppressed, its <c>FlowState</c>; a non-null annotation stays an annotation.</summary>
    Unproven,
}

/// <summary>How a result alternative relates to the coalescing expressions around it.</summary>
public enum ResultValueRole
{
    /// <summary>The alternative supplies the result as written.</summary>
    Direct,

    /// <summary>The left operand of <c>??</c>, which supplies the result only when it is not null.</summary>
    NonNullLeft,

    /// <summary>The right operand of <c>??</c>, which supplies the result as written.</summary>
    Fallback,
}
