using Microsoft.CodeAnalysis;

namespace CompilerSnapshot.Fixtures;

[Generator]
public sealed class FixtureGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(
            context.AnalyzerConfigOptionsProvider,
            (production, options) =>
            {
                if (
                    options.GlobalOptions.TryGetValue("build_property.FailGenerator", out var fail)
                    && fail == "true"
                )
                {
                    throw new InvalidOperationException("Requested generator failure.");
                }

                production.AddSource(
                    "Generated.g.cs",
                    "namespace Fixture; public class Generated : IContract { public string Value => string.Empty; }"
                );
            }
        );
    }
}
