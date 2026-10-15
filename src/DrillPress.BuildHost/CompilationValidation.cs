using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Project = Microsoft.CodeAnalysis.Project;

namespace DrillPress.BuildHost;

internal static class CompilationValidation
{
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
        // Suppressor exceptions can surface after diagnostics are collected, so capture them explicitly.
        var exceptions = new ConcurrentQueue<Diagnostic>();
        var diagnostics =
            suppressors.Length == 0
                ? compilation.GetDiagnostics(cancellationToken)
                : await compilation
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
        // Diagnostics reported outside the compilation still follow the project's severity configuration.
        var external = CompilationWithAnalyzers.GetEffectiveDiagnostics(
            failures.Select(LoadFailure).Concat(exceptions.Distinct()),
            compilation
        );
        ThrowErrors(compilation, diagnostics.Concat(external));
    }

    // Real builds report analyzers that cannot be created as this configurable warning.
    private static Diagnostic LoadFailure(string message) =>
        Diagnostic.Create(
            "CS8032",
            "Compiler",
            message,
            DiagnosticSeverity.Warning,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            warningLevel: 1
        );

    private static void ThrowErrors(
        CSharpCompilation compilation,
        IEnumerable<Diagnostic> diagnostics
    )
    {
        var errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                $"Compilation '{compilation.AssemblyName}' has {errors.Length} errors: {string.Join("; ", errors.Take(5).Select(error => error.ToString()))}"
            );
        }
    }
}
