namespace DrillPress;

/// <summary>Selects analysis candidates and composes reusable filtering conditions.</summary>
public sealed class CodeQuery<T>
{
    private readonly Func<AnalysisSolution, IEnumerable<T>> _select;
    private readonly RuleCondition<T>? _condition;

    internal CodeQuery(
        Func<AnalysisSolution, IEnumerable<T>> select,
        RuleCondition<T>? condition = null)
    {
        _select = select;
        _condition = condition;
    }

    /// <summary>Returns a query restricted to candidates that satisfy <paramref name="condition"/>.</summary>
    public CodeQuery<T> Where(RuleCondition<T> condition) =>
        new(
            _select,
            _condition is null
                ? condition
                : new RuleCondition<T>(candidate =>
                    _condition.Evaluate(candidate) && condition.Evaluate(candidate)));

    /// <summary>Excludes candidates satisfying an explicit exception.</summary>
    public CodeQuery<T> ExceptWhen(RuleCondition<T> condition) => Where(condition.Not());

    internal IEnumerable<T> Evaluate(AnalysisSolution solution)
    {
        var candidates = _select(solution);
        return _condition is null ? candidates : candidates.Where(_condition.Evaluate);
    }
}
