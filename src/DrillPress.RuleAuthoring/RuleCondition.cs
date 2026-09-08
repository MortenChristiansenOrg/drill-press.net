namespace DrillPress;

/// <summary>A reusable predicate; composition preserves short-circuit Boolean semantics.</summary>
/// <param name="predicate">The condition evaluated for each selected candidate.</param>
public sealed class RuleCondition<T>(Func<T, bool> predicate)
{
    /// <summary>Requires both this condition and the supplied condition.</summary>
    public RuleCondition<T> And(RuleCondition<T> other) => new(candidate => Evaluate(candidate) && other.Evaluate(candidate));

    /// <summary>Accepts either this condition or the supplied condition.</summary>
    public RuleCondition<T> Or(RuleCondition<T> other) => new(candidate => Evaluate(candidate) || other.Evaluate(candidate));

    /// <summary>Inverts this condition without restricting candidate discovery.</summary>
    public RuleCondition<T> Not() => new(candidate => !Evaluate(candidate));

    /// <summary>Accepts this condition only when the exception is false.</summary>
    public RuleCondition<T> ExceptWhen(RuleCondition<T> exception) => And(exception.Not());

    internal bool Evaluate(T candidate) => predicate(candidate);
}
