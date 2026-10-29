using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.Engine;

internal static class GeneratorExecution
{
    internal static GeneratorDriver Run(
        string project,
        GeneratorDriver driver,
        CSharpCompilation input,
        CancellationToken cancellationToken
    )
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(
            input,
            out _,
            out var diagnostics,
            cancellationToken
        );
        var failure = driver
            .GetRunResult()
            .Results.FirstOrDefault(result => result.Exception is not null);
        if (
            failure.Exception is not null
            || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        )
            throw new InvalidOperationException(
                $"Source generation failed in '{project}': {failure.Exception?.Message ?? string.Join("; ", diagnostics)}"
            );
        return driver;
    }
}
