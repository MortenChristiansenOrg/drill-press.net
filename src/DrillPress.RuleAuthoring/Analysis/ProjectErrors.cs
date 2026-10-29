using Microsoft.CodeAnalysis;

namespace DrillPress;

// A broad operation query will inspect most of the project. In that case one compiler pass
// avoids compiling its method bodies again for every document's error check. Narrow queries
// keep using document diagnostics, and a project with errors retains the exact fallback.
internal sealed class ProjectErrors
{
    private readonly AnalysisProject _project;
    private readonly Lazy<bool> _hasErrors;
    private bool _wholeProject;

    internal ProjectErrors(AnalysisProject project, Lazy<bool>? compilerErrors = null)
    {
        _project = project;
        _hasErrors =
            compilerErrors
            ?? new(() =>
                project
                    .Compilation.GetDiagnostics(project.CancellationToken)
                    .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            );
        _wholeProject = compilerErrors is not null;
    }

    internal void ConsiderWholeProject()
    {
        if (_wholeProject)
            return;
        var ordinary = _project.Sources.Where(source => !source.Document.IsGenerated).ToArray();
        if (
            ordinary.Length >= 8
            && ordinary.All(source => source.Operations.IsBound)
            && ordinary.Sum(source => (long)source.Document.Text.Length)
                >= _project
                    .Sources.Where(source => source.Document.IsGenerated)
                    .Sum(source => (long)source.Document.Text.Length)
        )
            _wholeProject = true;
    }

    internal bool InDocument(AnalysisSource source) =>
        (!_wholeProject || _hasErrors.Value)
        && source
            .Model.GetDiagnostics(cancellationToken: _project.CancellationToken)
            .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
