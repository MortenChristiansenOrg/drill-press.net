namespace DrillPress;

/// <summary>Concise predicate syntax for ordinary C# rule declarations. Explicit RuleCondition values additionally support Boolean composition.</summary>
public static class QueryPredicates
{
    /// <summary>Filters a selection using a pure predicate, cached per query and solution.</summary>
    public static CodeQuery<T> Where<T>(this CodeQuery<T> query, Func<T, bool> predicate) =>
        query.Where(new RuleCondition<T>(predicate));

    /// <summary>Removes an explicitly configured exception from a selection.</summary>
    public static CodeQuery<T> ExceptWhen<T>(this CodeQuery<T> query, Func<T, bool> predicate) =>
        query.ExceptWhen(new RuleCondition<T>(predicate));

    /// <summary>Reports candidates that do not satisfy a readable requirement.</summary>
    public static RuleScope<T> Require<T>(
        this RuleScope<T> scope,
        Func<T, bool> predicate,
        string id,
        string message,
        Func<T, SourceLocation>? location = null,
        Func<T, FixProposal?>? fix = null
    ) => scope.Require(new RuleCondition<T>(predicate), id, message, location, fix);
}
