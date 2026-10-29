using Microsoft.CodeAnalysis;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class ConstantSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context) =>
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource("Generated.g.cs", "public class Generated { }")
        );
}
