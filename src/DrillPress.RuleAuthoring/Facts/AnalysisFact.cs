namespace DrillPress.Facts;

/// <summary>A typed, lazy fact owned by one solution. Reuse the same instance across rules; dependencies may read other facts, but cycles are rejected by lazy evaluation.</summary>
public sealed class AnalysisFact<T>(Func<AnalysisSolution, T> compute) where T : notnull
{
    /// <summary>Computes once for the supplied solution. Values should be immutable; failures are cached for that solution.</summary>
    public T In(AnalysisSolution solution) => solution.Cached(this, () => compute(solution));

    /// <summary>Exposes a fact's candidates as a reusable query.</summary>
    public CodeQuery<TCandidate> SelectMany<TCandidate>(Func<T, IEnumerable<TCandidate>> select) =>
        CodeQuery<TCandidate>.Create(solution => select(In(solution)));
}
