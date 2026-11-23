using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DrillPress;

// A compilation's errors, keeping apart the warnings reported as errors that a project suppressor may hide, such as
// EF Core's CS8618 on DbSet properties. Rules may run as native code, which cannot load the project's suppressors,
// so fix validation tolerates those errors as long as an edit adds none.
internal sealed class CompilationErrors
{
    private readonly Dictionary<
        (string Path, string Id, string Message),
        List<Diagnostic>
    > _suppressible;

    private CompilationErrors(
        Diagnostic? blocking,
        Dictionary<(string Path, string Id, string Message), List<Diagnostic>> suppressible
    )
    {
        Blocking = blocking;
        _suppressible = suppressible;
    }

    /// <summary>The first error that no suppressor of the project can hide, or null.</summary>
    internal Diagnostic? Blocking { get; }

    internal static CompilationErrors Read(AnalysisProject project, Compilation compilation)
    {
        var ids = project.Snapshot.SuppressibleDiagnosticIds;
        Diagnostic? blocking = null;
        var suppressible = new Dictionary<(string, string, string), List<Diagnostic>>();
        foreach (var error in compilation.GetDiagnostics(project.CancellationToken))
        {
            if (error.Severity != DiagnosticSeverity.Error)
                continue;
            if (!error.IsWarningAsError || !ids.Contains(error.Id))
            {
                blocking = error;
                break;
            }

            var key = (
                error.Location.SourceTree?.FilePath ?? "",
                error.Id,
                error.GetMessage(CultureInfo.InvariantCulture)
            );
            if (!suppressible.TryGetValue(key, out var same))
                suppressible.Add(key, same = []);
            same.Add(error);
        }

        return new(blocking, suppressible);
    }

    /// <summary>The first error this edited compilation adds to the original: one no suppressor can hide, or a suppressible one beyond those the same file already reports with the same message.</summary>
    internal Diagnostic? AddedTo(CompilationErrors original) =>
        Blocking
        ?? _suppressible
            .Select(pair =>
                pair.Value.ElementAtOrDefault(
                    original._suppressible.GetValueOrDefault(pair.Key)?.Count ?? 0
                )
            )
            .FirstOrDefault(error => error is not null);
}
