using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Project = Microsoft.CodeAnalysis.Project;

namespace DrillPress.BuildHost;

internal static class ProjectAnalyzers
{
    public static T[] Load<T>(Project project, Func<AnalyzerReference, IEnumerable<T>> select)
    {
        var (loaded, failures) = TryLoad(project, select);
        if (failures.Length > 0)
            throw LoadFailure(project, failures);
        return loaded;
    }

    public static (T[] Loaded, string[] Failures) TryLoad<T>(
        Project project,
        Func<AnalyzerReference, IEnumerable<T>> select
    )
    {
        var failures = new List<string>();
        void Record(object? sender, AnalyzerLoadFailureEventArgs args) =>
            failures.Add(args.Message);
        var references = project.AnalyzerReferences.OfType<AnalyzerFileReference>().ToArray();
        foreach (var reference in references)
            reference.AnalyzerLoadFailed += Record;
        try
        {
            return (project.AnalyzerReferences.SelectMany(select).ToArray(), failures.ToArray());
        }
        finally
        {
            foreach (var reference in references)
                reference.AnalyzerLoadFailed -= Record;
        }
    }

    public static InvalidOperationException LoadFailure(
        Project project,
        IEnumerable<string> failures
    ) => new($"Analyzer loading failed in '{project.Name}': {string.Join("; ", failures)}");
}
