namespace DrillPress;

/// <summary>Selects analysis candidates and composes reusable filtering conditions.</summary>
public sealed class CodeQuery<T>
{
    private readonly Func<AnalysisSolution, IReadOnlySet<string>?, IEnumerable<T>> _select;
    private readonly RuleCondition<T>? _condition;

    /// <summary>Defines a lazy consumer selection, materialized once per solution and query instance. Selectors must be pure and observe solution cancellation.</summary>
    public static CodeQuery<T> Create(Func<AnalysisSolution, IEnumerable<T>> select)
    {
        var root = new CodeQuery<T>(select);
        return new(solution => root.In(solution));
    }

    /// <summary>Projects candidates while retaining lazy, solution-scoped evaluation.</summary>
    public CodeQuery<TResult> Select<TResult>(Func<T, TResult> select) =>
        CodeQuery<TResult>.Create(solution => Evaluate(solution).Select(select));

    /// <summary>Expands each candidate into independently reportable children.</summary>
    public CodeQuery<TResult> SelectMany<TResult>(Func<T, IEnumerable<TResult>> select) =>
        CodeQuery<TResult>.Create(solution => Evaluate(solution).SelectMany(select));

    /// <summary>Anchors synthetic facts or joined results to an existing reportable owner.</summary>
    public CodeQuery<LocatedCandidate<T>> At(Func<T, ICodeElement> anchor) => Select(value => new LocatedCandidate<T>(value, anchor(value)));

    /// <summary>Returns candidates lacking a matching counterpart; diagnostics can remain anchored to the original candidate.</summary>
    public CodeQuery<T> WithoutMatching<TOther, TKey>(CodeQuery<TOther> other, Func<T, TKey> key,
        Func<TOther, TKey> otherKey, IEqualityComparer<TKey>? comparer = null) => Create(solution =>
    {
        var keys = other.Evaluate(solution).Select(otherKey).ToHashSet(comparer);
        return Evaluate(solution).Where(candidate => !keys.Contains(key(candidate)));
    });

    /// <summary>Returns candidates with no counterpart satisfying a contextual relationship. Prefer keyed matching for large inventories with simple equality.</summary>
    public CodeQuery<T> WithoutMatching<TOther>(CodeQuery<TOther> other, Func<T, TOther, bool> matches) => Create(solution =>
    {
        var counterparts = other.In(solution);
        return In(solution).Where(candidate => !counterparts.Any(otherCandidate => matches(candidate, otherCandidate)));
    });

    /// <summary>Joins two selections using explicit keys; include compilation context in keys when matching semantic identities.</summary>
    public CodeQuery<TResult> Join<TOther, TKey, TResult>(CodeQuery<TOther> other, Func<T, TKey> key,
        Func<TOther, TKey> otherKey, Func<T, TOther, TResult> select, IEqualityComparer<TKey>? comparer = null) =>
        CodeQuery<TResult>.Create(solution => Evaluate(solution).Join(other.Evaluate(solution), key, otherKey, select, comparer));

    /// <summary>Reads this selection once per analysis, also allowing custom facts to compose built-in queries.</summary>
    public IReadOnlyList<T> In(AnalysisSolution solution) => solution.Cached(this, () => Materialize(solution));

    private IReadOnlyList<T> Materialize(AnalysisSolution solution)
    {
        var result = new List<T>();
        foreach (var candidate in SelectCandidates(solution))
        {
            solution.CancellationToken.ThrowIfCancellationRequested();
            result.Add(candidate);
        }

        return result.AsReadOnly();
    }

    internal CodeQuery(
        Func<AnalysisSolution, IEnumerable<T>> select,
        RuleCondition<T>? condition = null)
        : this((solution, _) => select(solution), condition) { }

    internal CodeQuery(
        Func<AnalysisSolution, IReadOnlySet<string>?, IEnumerable<T>> select,
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
                : _condition.And(condition));

    /// <summary>Excludes candidates satisfying an explicit exception.</summary>
    public CodeQuery<T> ExceptWhen(RuleCondition<T> condition) => Where(condition.Not());

    internal IEnumerable<T> Evaluate(AnalysisSolution solution) => In(solution);

    private IEnumerable<T> SelectCandidates(AnalysisSolution solution)
    {
        var candidates = _select(solution, _condition?.MemberNames);
        return _condition is null ? candidates : candidates.Where(_condition.Evaluate);
    }
}
