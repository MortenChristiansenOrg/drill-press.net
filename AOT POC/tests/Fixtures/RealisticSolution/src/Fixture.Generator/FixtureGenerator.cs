using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Fixture.Generator;

[Generator]
public sealed class FixtureGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var value = context.AdditionalTextsProvider
            .Where(static file => Path.GetFileName(file.Path) == "GeneratorInput.txt")
            .Select(static (file, cancellationToken) =>
                file.GetText(cancellationToken)?.ToString().Trim() ?? "missing")
            .Collect()
            .Select(static (values, _) => values.FirstOrDefault() ?? "missing");

        context.RegisterSourceOutput(value, static (productionContext, input) =>
        {
            var escaped = input.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
            productionContext.AddSource(
                "GeneratedMarker.g.cs",
                SourceText.From(
                    $$"""
                    namespace Fixture.Library;

                    public static class GeneratedMarker
                    {
                        public const string Value = "{{escaped}}";

                        public static string Empty => string.Empty;
                    }
                    """,
                    Encoding.UTF8));
        });
    }
}
