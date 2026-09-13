using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.BuildHost;

internal static class CompilationValidation
{
    public static void Validate(
        CSharpCompilation compilation,
        bool validate,
        CancellationToken cancellationToken
    )
    {
        if (!validate)
        {
            return;
        }

        var errors = compilation
            .GetDiagnostics(cancellationToken)
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
