namespace DrillPress;

/// <summary>Lambda filters for any selection. Reusable <see cref="RuleCondition{T}"/> values additionally compose with And, Or and Not.</summary>
public static class QueryPredicates
{
    /// <summary>Keeps candidates satisfying a pure predicate; results are cached per query and analysis.</summary>
    public static CodeQuery<T> Where<T>(this CodeQuery<T> query, Func<T, bool> predicate) =>
        query.Where(new RuleCondition<T>(predicate));

    /// <summary>Removes candidates matching an explicit exception.</summary>
    public static CodeQuery<T> ExceptWhen<T>(this CodeQuery<T> query, Func<T, bool> predicate) =>
        query.ExceptWhen(new RuleCondition<T>(predicate));
}
