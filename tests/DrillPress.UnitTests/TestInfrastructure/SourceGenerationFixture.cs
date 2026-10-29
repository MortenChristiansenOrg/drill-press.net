using System.IO.Abstractions.TestingHelpers;
using DrillPress.Engine;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class SourceGenerationFixture
{
    internal const string Fingerprint =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    internal MockFileSystem FileSystem { get; } = new();
    internal GeneratorTransport Transport { get; }
    internal CompilationSnapshot Snapshot { get; }
    internal StringWriter Output { get; } = new();
    internal StringWriter Error { get; } = new();

    internal SourceGenerationFixture()
    {
        FileSystem.Directory.CreateDirectory("/project/generation");
        FileSystem.Directory.SetCurrentDirectory("/project");
        FileSystem.AddFile("/project/Use.cs", "public class Use : Generated { }");
        Snapshot = CompilationSnapshot.Create(
            TestSnapshots.CreateProject("/project/Use.cs", "public class Use : Generated { }") with
            {
                ContextId = "source",
            }
        );
        Transport = new GeneratorTransport(FileSystem, "/project/generation");
    }

    internal async Task ArrangeAsync(
        CompiledSourceGenerator generator,
        ProjectSnapshot? project = null
    )
    {
        var selected = project ?? Snapshot.Projects.Single();
        await Transport.WritePlanAsync(
            new GeneratorProjectPlan(
                selected,
                new GeneratorInputs([generator.Description], [], [], [])
            ),
            TestContext.Current.CancellationToken
        );
        await new CompilationSnapshotFile(FileSystem).WriteAsync(
            "/project/raw.json",
            Snapshot with
            {
                Projects = [selected],
            },
            TestContext.Current.CancellationToken
        );
    }

    internal async Task<RuleExitCode> RunAsync(SourceGeneratorCatalog catalog)
    {
        var rules = new RuleCatalog();
        rules
            .Rule("GEN001", "Generated type is available.")
            .For(
                Code.Types.Named("Use")
                    .Where(type => type.Source.Project.HasType(CodeType.Named("Generated")))
            )
            .Forbid();
        var policy = new SourceFilePolicy(FileSystem, new StubFileIdentityProbe());
        return await new RuleApplication(
            FileSystem,
            new CoverageProcess(),
            policy,
            catalog
        ).RunAsync(
            rules,
            [
                "check-generated",
                "/project/generation",
                "/project/raw.json",
                "/project/effective.json",
                "--parallel-queries",
            ],
            Output,
            Error,
            TestContext.Current.CancellationToken
        );
    }

    internal async Task<string> RenderAsync()
    {
        var snapshot = await new CompilationSnapshotFile(FileSystem).ReadAsync(
            "/project/effective.json",
            TestContext.Current.CancellationToken
        );
        return new CompactDiagnosticRenderer(FileSystem).Render(
            BundleResponseProtocol.Read(Output.ToString(), snapshot)
        );
    }

    internal async Task<GeneratorFallbackRequest> RespondAsync()
    {
        var request = await Transport.ReadRequestAsync(
            "source",
            TestContext.Current.CancellationToken
        );
        await Transport.WriteResponseAsync(
            "source",
            new GeneratorFallbackResponse([
                new GeneratedSourceData(
                    0,
                    "Generated.g.cs",
                    "Generator/Generated.g.cs",
                    "public class Generated { }",
                    "utf-8",
                    1
                ),
            ]),
            TestContext.Current.CancellationToken
        );
        return request;
    }
}
