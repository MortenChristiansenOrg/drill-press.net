namespace DrillPress.Relationships;

internal sealed class CompilationViews(AnalysisSolution solution)
{
    private readonly Dictionary<string, AnalysisProject[]> _closures = [];
    private readonly Dictionary<string, AnalysisProject[][]> _views = [];

    public bool Reaches(AnalysisProject project, string contextId) =>
        Closure(project).Any(dependency => dependency.Snapshot.ContextId == contextId);

    public AnalysisProject[][] For(AnalysisProject owner)
    {
        if (_views.TryGetValue(owner.Snapshot.ContextId, out var cached))
        {
            return cached;
        }

        var consumers = solution
            .Projects.Where(project => Reaches(project, owner.Snapshot.ContextId))
            .ToArray();
        var roots = consumers
            .Where(project =>
                !consumers.Any(other =>
                    other != project && Reaches(other, project.Snapshot.ContextId)
                )
            )
            .ToArray();
        var result = new List<AnalysisProject[]>();
        foreach (var framework in roots.GroupBy(project => project.Snapshot.TargetFramework))
        {
            Expand([], framework.ToList(), [], result);
        }

        cached = result.ToArray();
        _views.Add(owner.Snapshot.ContextId, cached);
        return cached;
    }

    // Enumerate maximal compatible root sets. This preserves alternate dependency evaluations,
    // even when their consuming projects happen to target the same framework.
    private void Expand(
        List<AnalysisProject> selected,
        List<AnalysisProject> candidates,
        List<AnalysisProject> excluded,
        List<AnalysisProject[]> result
    )
    {
        solution.CancellationToken.ThrowIfCancellationRequested();
        if (candidates.Count == 0 && excluded.Count == 0)
        {
            result.Add(selected.SelectMany(Closure).Distinct().ToArray());
            return;
        }

        var pivot = candidates
            .Concat(excluded)
            .MaxBy(project =>
                candidates.Count(candidate =>
                    candidate != project && Compatible(project, candidate)
                )
            );
        var choices = candidates
            .Where(candidate =>
                candidate == pivot || pivot is null || !Compatible(candidate, pivot)
            )
            .ToArray();
        foreach (var candidate in choices)
        {
            Expand(
                [.. selected, candidate],
                candidates
                    .Where(other => other != candidate && Compatible(candidate, other))
                    .ToList(),
                excluded.Where(other => Compatible(candidate, other)).ToList(),
                result
            );
            candidates.Remove(candidate);
            excluded.Add(candidate);
        }
    }

    private bool Compatible(AnalysisProject left, AnalysisProject right)
    {
        var first = Closure(left);
        var second = Closure(right);
        return first.All(project =>
            second.All(other =>
                project.Snapshot.ProjectPath != other.Snapshot.ProjectPath
                || project.Snapshot.ContextId == other.Snapshot.ContextId
            )
        );
    }

    private AnalysisProject[] Closure(AnalysisProject root)
    {
        if (_closures.TryGetValue(root.Snapshot.ContextId, out var cached))
        {
            return cached;
        }

        var seen = new HashSet<string>();
        var pending = new Stack<AnalysisProject>();
        var result = new List<AnalysisProject>();
        pending.Push(root);
        while (pending.TryPop(out var project))
        {
            if (!seen.Add(project.Snapshot.ContextId))
            {
                continue;
            }

            result.Add(project);
            foreach (var id in project.Snapshot.ReferencedContextIds)
            {
                pending.Push(
                    solution.Projects.Single(candidate => candidate.Snapshot.ContextId == id)
                );
            }
        }

        cached = result.ToArray();
        _closures.Add(root.Snapshot.ContextId, cached);
        return cached;
    }
}
