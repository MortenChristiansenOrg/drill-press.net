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
        var handlers = project
            .AnalyzerReferences.OfType<AnalyzerFileReference>()
            .Select(reference =>
            {
                // Matches the compiler's CS8032 wording when a named analyzer cannot be created.
                EventHandler<AnalyzerLoadFailureEventArgs> record = (_, args) =>
                    failures.Add(
                        args.TypeName is null
                            ? args.Message
                            : $"An instance of analyzer {args.TypeName} cannot be created from {reference.FullPath}: {args.Message}"
                    );
                reference.AnalyzerLoadFailed += record;
                return (Reference: reference, Record: record);
            })
            .ToArray();
        try
        {
            return (project.AnalyzerReferences.SelectMany(select).ToArray(), failures.ToArray());
        }
        finally
        {
            foreach (var (reference, record) in handlers)
                reference.AnalyzerLoadFailed -= record;
        }
    }

    public static InvalidOperationException LoadFailure(
        Project project,
        IEnumerable<string> failures
    ) => new($"Analyzer loading failed in '{project.Name}': {string.Join("; ", failures)}");
}
