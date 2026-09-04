namespace DrillPress;

/// <summary>Encapsulates a reusable predicate that can filter rule candidates.</summary>
/// <param name="predicate">The predicate evaluated for each candidate.</param>
public sealed class RuleCondition<T>(Func<T, bool> predicate)
{
    internal bool Evaluate(T candidate) => predicate(candidate);
}
