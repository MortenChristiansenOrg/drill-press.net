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

        var suppressors = ProjectAnalyzers.Load(
            project,
            reference => reference.GetAnalyzers(LanguageNames.CSharp).OfType<DiagnosticSuppressor>()
        );
        ThrowErrors(
            compilation,
            suppressors.Length == 0
                ? compilation.GetDiagnostics(cancellationToken)
                : await compilation
                    .WithAnalyzers([.. suppressors], project.AnalyzerOptions)
                    .GetAllDiagnosticsAsync(cancellationToken)
        );
    }

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
