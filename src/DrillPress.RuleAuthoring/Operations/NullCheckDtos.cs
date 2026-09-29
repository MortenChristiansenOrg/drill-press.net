namespace DrillPress;

/// <summary>The semantic domain of a bound null test; nullable value presence is not reference nullability.</summary>
public enum NullCheckDomain
{
    /// <summary>A reference or unconstrained type-parameter null test.</summary>
    Reference,

    /// <summary>A nullable value-type presence test.</summary>
    NullableValue,
}

/// <summary>Which state makes the complete normalized Boolean check true.</summary>
public enum NullCheckPolarity
{
    /// <summary>True means the operand is null or has no nullable value.</summary>
    IsNull,

    /// <summary>True means the operand is non-null or has a nullable value.</summary>
    IsNotNull,
}

/// <summary>The underlying bound syntax form, independent of normalized negation.</summary>
public enum NullCheckForm
{
    /// <summary>Built-in equality or inequality with null.</summary>
    Equality,

    /// <summary>A null constant pattern, including negated patterns.</summary>
    Pattern,

    /// <summary>A nullable value's HasValue property.</summary>
    NullableHasValue,
}
