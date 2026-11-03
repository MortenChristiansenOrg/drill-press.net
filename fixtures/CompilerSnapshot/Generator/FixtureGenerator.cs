using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

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
                // Either property is "true" or names the target framework it applies to.
                if (Requested(options, "FailGenerator"))
                {
                    throw new InvalidOperationException("Requested generator failure.");
                }

                // Blocks until the export cancels this context.
                if (Requested(options, "BlockGenerator"))
                {
                    production.CancellationToken.WaitHandle.WaitOne();
                    production.CancellationToken.ThrowIfCancellationRequested();
                }

                production.AddSource(
                    "Generated.g.cs",
                    "namespace Fixture; public class Generated : IContract { public string Value => string.Empty; }"
                );
            }
        );
        // One output per .fixture additional file, in the order the compiler receives them.
        context.RegisterSourceOutput(
            context.AdditionalTextsProvider.Where(text => text.Path.EndsWith(".fixture")),
            (production, text) =>
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(text.Path);
                production.AddSource($"{name}.fixture.g.cs", $"// {name}");
            }
        );
        // The compiler gives generators referenced projects as assemblies, without source declarations.
        context.RegisterSourceOutput(
            context.CompilationProvider,
            (production, compilation) =>
            {
                if (
                    compilation
                        .GetTypeByMetadataName("Clock")
                        ?.GetMembers("Now")
                        .SingleOrDefault() is
                    { } now
                )
                {
                    production.AddSource(
                        "Clock.g.cs",
                        $"// Clock.Now has {now.DeclaringSyntaxReferences.Length} source declarations"
                    );
                }
            }
        );
    }

    private static bool Requested(AnalyzerConfigOptionsProvider options, string property) =>
        options.GlobalOptions.TryGetValue("build_property." + property, out var value)
        && (
            value == "true"
            || (
                options.GlobalOptions.TryGetValue(
                    "build_property.TargetFramework",
                    out var framework
                )
                && value == framework
            )
        );
}
