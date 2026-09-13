namespace DrillPress.Facts;

/// <summary>A typed fact computed on demand once for each evaluated project within one analysis. Alternate frameworks never share values.</summary>
public sealed class ProjectFact<T>(Func<AnalysisProject, T> compute)
    where T : notnull
{
    /// <summary>Reads the fact for a project belonging to this solution, preserving cancellation and caching failures in that context.</summary>
    public T In(AnalysisSolution solution, AnalysisProject project)
    {
        if (!solution.Projects.Contains(project))
        {
            throw new ArgumentException(
                "The project must belong to the supplied solution.",
                nameof(project)
            );
        }

        var cache = solution.Cached(this, () => new Dictionary<AnalysisProject, Lazy<T>>());
        Lazy<T> value;
        lock (cache)
        {
            if (!cache.TryGetValue(project, out value!))
            {
                value = new(() => compute(project));
                cache.Add(project, value);
            }
        }

        return value.Value;
    }
}
