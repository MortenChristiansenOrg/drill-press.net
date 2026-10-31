namespace DrillPress;

/// <summary>The alternatives that can supply an expression's result, with explicit boundaries where analysis stopped. Nothing is executed and no runtime certainty is implied.</summary>
/// <param name="Status">Whether every alternative was classified within the requested bounds.</param>
/// <param name="Values">Result alternatives in source order. Conditions, call arguments and throw arms are not results.</param>
/// <param name="Boundaries">Invalid binding, user-defined conversions, dynamic or unsupported expressions, and exhausted bounds.</param>
public sealed record ResultValueAnalysis(
    ExpressionTraversalStatus Status,
    IReadOnlyList<ResultValue> Values,
    IReadOnlyList<ExpressionTraversalBoundary> Boundaries
)
{
    /// <summary>Whether every alternative was classified.</summary>
    public bool IsComplete => Status == ExpressionTraversalStatus.Complete;

    /// <summary>Whether analysis completed, found a result, and language rules alone rule out null for every alternative.</summary>
    public bool IsNonNullByLanguage =>
        IsComplete && Values.Count > 0 && Values.All(value => value.IsNonNullByLanguage);
}
