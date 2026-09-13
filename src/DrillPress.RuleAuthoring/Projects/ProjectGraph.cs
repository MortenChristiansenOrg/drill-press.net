namespace DrillPress.Projects;

/// <summary>Context-aware project relationships using captured MSBuild source edges, not project or assembly names.</summary>
public sealed class ProjectGraph(AnalysisSolution solution)
{
    private readonly AnalysisSolution _solution = solution;
    private readonly CompilationViews _views = new(solution);

    /// <summary>Tests whether a project is the owner itself or transitively references its exact evaluated context.</summary>
    public bool Includes(AnalysisProject consumer, AnalysisProject owner) =>
        _views.Reaches(consumer, owner.Snapshot.ContextId);

    /// <summary>Returns loaded dependency contexts including the project itself, preserving solution order.</summary>
    public IReadOnlyList<AnalysisProject> DependenciesOf(AnalysisProject project) =>
        _solution.Projects.Where(candidate => Includes(project, candidate)).ToArray();

    /// <summary>Returns separate maximal compatible views containing this owner. Alternate frameworks and incompatible evaluations are never merged.</summary>
    public IReadOnlyList<IReadOnlyList<AnalysisProject>> CompatibleViewsOf(AnalysisProject owner) =>
        _views
            .For(owner)
            .Select(view => (IReadOnlyList<AnalysisProject>)Array.AsReadOnly(view))
            .ToArray();
}
