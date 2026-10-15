using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Project = Microsoft.CodeAnalysis.Project;

namespace DrillPress.BuildHost;

internal static class CompilationValidation
{
    // The compiler reports analyzers that cannot be constructed as this configurable warning.
    private const string AnalyzerLoadFailureId = "CS8032";

    public static void Validate(
        CSharpCompilation compilation,
        bool validate,
        CancellationToken cancellationToken
    )
    {
        if (validate)
            ThrowErrors(compilation, compilation.GetDiagnostics(cancellationToken));
    }

    // Package suppressors hide configurable diagnostics in real builds, such as CS8618 on EF Core DbSet properties.
    public static async Task ValidateAsync(
        CSharpCompilation compilation,
        Project project,
        bool validate,
        CancellationToken cancellationToken
    )
    {
        if (!validate)
            return;

        var (suppressors, failures) = ProjectAnalyzers.TryLoad(
            project,
            reference => reference.GetAnalyzers(LanguageNames.CSharp).OfType<DiagnosticSuppressor>()
        );
        if (
            failures.Length > 0
            && IsError(AnalyzerLoadFailureId, DiagnosticSeverity.Warning, compilation.Options)
        )
            throw ProjectAnalyzers.LoadFailure(project, failures);
        if (suppressors.Length == 0)
        {
            ThrowErrors(compilation, compilation.GetDiagnostics(cancellationToken));
            return;
        }

        // Suppressor exceptions can surface after diagnostics are collected, so capture them explicitly.
        var exceptions = new ConcurrentQueue<Diagnostic>();
        var diagnostics = await compilation
            .WithAnalyzers(
                [.. suppressors],
                new CompilationWithAnalyzersOptions(
                    project.AnalyzerOptions,
                    (_, _, diagnostic) => exceptions.Enqueue(diagnostic),
                    concurrentAnalysis: true,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false
                )
            )
            .GetAllDiagnosticsAsync(cancellationToken);
        ThrowErrors(compilation, diagnostics.Concat(exceptions).Distinct());
    }

    private static void ThrowErrors(
        CSharpCompilation compilation,
        IEnumerable<Diagnostic> diagnostics
    )
    {
        var errors = diagnostics
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
                || IsError(diagnostic.Id, diagnostic.Severity, compilation.Options)
            )
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                $"Compilation '{compilation.AssemblyName}' has {errors.Length} errors: {string.Join("; ", errors.Take(5).Select(error => error.ToString()))}"
            );
        }
    }

    // Mirrors WarningsAsErrors, NoWarn and TreatWarningsAsErrors for diagnostics reported outside the compilation.
    private static bool IsError(
        string id,
        DiagnosticSeverity severity,
        CompilationOptions options
    ) =>
        options.SpecificDiagnosticOptions.TryGetValue(id, out var report)
        && report != ReportDiagnostic.Default
            ? report == ReportDiagnostic.Error
            : severity == DiagnosticSeverity.Warning
                && options.GeneralDiagnosticOption == ReportDiagnostic.Error;
}
