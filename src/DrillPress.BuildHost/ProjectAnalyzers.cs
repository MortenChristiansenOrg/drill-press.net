using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Project = Microsoft.CodeAnalysis.Project;

namespace DrillPress.BuildHost;

internal static class ProjectAnalyzers
{
    public static T[] Load<T>(Project project, Func<AnalyzerReference, IEnumerable<T>> select)
    {
        var loadFailures = new List<string>();
        void Record(object? sender, AnalyzerLoadFailureEventArgs args) =>
            loadFailures.Add(args.Message);
        var references = project.AnalyzerReferences.OfType<AnalyzerFileReference>().ToArray();
        foreach (var reference in references)
            reference.AnalyzerLoadFailed += Record;
        try
        {
            var loaded = project.AnalyzerReferences.SelectMany(select).ToArray();
            if (loadFailures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Analyzer loading failed in '{project.Name}': {string.Join("; ", loadFailures)}"
                );
            }

            return loaded;
        }
        finally
        {
            foreach (var reference in references)
                reference.AnalyzerLoadFailed -= Record;
        }
    }
}
