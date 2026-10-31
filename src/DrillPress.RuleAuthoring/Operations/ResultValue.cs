namespace DrillPress;

/// <summary>One expression that can supply a result, with its evidence kept apart so a rule can apply its own policy.</summary>
/// <param name="Anchor">The written alternative, including its parentheses and suppression; use it for reporting.</param>
/// <param name="Expression">The underlying expression, whose <c>DeclaredNullability</c> and <c>Kind</c> are not masked by suppression. The compiler also reports its <c>FlowState</c> as not null when suppressed, so read <paramref name="IsSuppressed"/> before relying on flow state.</param>
/// <param name="Kind">What the language establishes about the underlying expression.</param>
/// <param name="Role">How surrounding coalescing expressions treat this alternative.</param>
/// <param name="IsSuppressed">Whether a null-forgiving operator applies to this alternative.</param>
/// <param name="IsCast">Whether a built-in explicit cast applies to this alternative.</param>
public sealed record ResultValue(
    CodeExpression Anchor,
    CodeExpression Expression,
    ResultValueKind Kind,
    ResultValueRole Role,
    bool IsSuppressed,
    bool IsCast
)
{
    /// <summary>Whether this alternative cannot supply null by language rules alone. Annotations, flow state and suppression don't count.</summary>
    public bool IsNonNullByLanguage =>
        Role == ResultValueRole.NonNullLeft
        || Kind
            is ResultValueKind.Constant
                or ResultValueKind.Construction
                or ResultValueKind.ValueTypeDefault
                or ResultValueKind.ValueTypeValue;
}
