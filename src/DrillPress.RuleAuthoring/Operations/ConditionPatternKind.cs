namespace DrillPress.Operations;

/// <summary>Validation-check classification, without asserting that different kinds have equivalent runtime behavior.</summary>
public enum ConditionPatternKind
{
    /// <summary>A consumer-defined call predicate.</summary>
    Custom,

    /// <summary>Null or missing-value tests.</summary>
    Null,

    /// <summary>Empty-string comparisons, constant patterns and zero length.</summary>
    EmptyString,

    /// <summary>The framework String.IsNullOrEmpty predicate.</summary>
    NullOrEmptyString,

    /// <summary>The framework String.IsNullOrWhiteSpace predicate.</summary>
    NullOrWhiteSpaceString,
}
