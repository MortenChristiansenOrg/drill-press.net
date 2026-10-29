using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.UnitTests.Engine;

public sealed class SourceGenerationSessionTests
{
    [Fact]
    public async Task Generation_preserves_the_captured_source_when_the_live_file_changes()
    {
        var fixture = new SourceGenerationFixture();
        var generator = new CompiledSourceGenerator(
            new ConstantSourceGenerator().AsSourceGenerator(),
            SourceGenerationFixture.Fingerprint
        );
        var original = fixture.Snapshot.Projects.Single();
        var document = SourceIdentity.Capture(
            original.Documents.Single(),
            fixture.FileSystem.File.ReadAllBytes(fixture.SourcePath),
            "utf-8",
            false
        );
        await fixture.ArrangeAsync(generator, original with { Documents = [document] });
        fixture.FileSystem.File.WriteAllText(fixture.SourcePath, "public class Changed { }");

        var result = await fixture.RunAsync(new TestSourceGeneratorCatalog(generator));
        var snapshot = await new CompilationSnapshotFile(fixture.FileSystem).ReadAsync(
            fixture.EffectivePath,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(RuleExitCode.Findings, result);
        Assert.Equal("", fixture.Error.ToString());
        Assert.Equal(document.Fingerprint, snapshot.Projects.Single().Documents[0].Fingerprint);
        Assert.Equal(
            "public class Changed { }",
            fixture.FileSystem.File.ReadAllText(fixture.SourcePath)
        );
        Assert.Equal(
            """
            GEN001 Generated type is available.
            Use.cs
              1:14

            """.ReplaceLineEndings("\n"),
            await fixture.RenderAsync()
        );
    }

    [Fact]
    public async Task Compiled_generators_supply_types_to_ordinary_source_queries()
    {
        var fixture = new SourceGenerationFixture();
        var generator = new CompiledSourceGenerator(
            new ConstantSourceGenerator().AsSourceGenerator(),
            SourceGenerationFixture.Fingerprint
        );
        await fixture.ArrangeAsync(generator);

        var result = await fixture.RunAsync(new TestSourceGeneratorCatalog(generator));

        Assert.Equal(RuleExitCode.Findings, result);
        Assert.Equal("", fixture.Error.ToString());
        Assert.Equal(
            """
            GEN001 Generated type is available.
            Use.cs
              1:14

            """.ReplaceLineEndings("\n"),
            await fixture.RenderAsync()
        );
    }

    [Fact]
    public async Task Different_generator_bytes_request_managed_execution_instead_of_substituting_a_version()
    {
        var fixture = new SourceGenerationFixture();
        var requested = new CompiledSourceGenerator(
            new ConstantSourceGenerator().AsSourceGenerator(),
            new string('B', 64)
        );
        var linked = new CompiledSourceGenerator(
            new ConstantSourceGenerator().AsSourceGenerator(),
            SourceGenerationFixture.Fingerprint
        );
        await fixture.ArrangeAsync(requested);
        var fallback = fixture.RespondAsync();

        var result = await fixture.RunAsync(new TestSourceGeneratorCatalog(linked));

        Assert.Equal(RuleExitCode.Findings, result);
        Assert.Equal("", fixture.Error.ToString());
        Assert.Empty((await fallback).References);
        Assert.Empty((await fallback).SourceContexts);
        Assert.Equal(
            """
            GEN001 Generated type is available.
            Use.cs
              1:14

            """.ReplaceLineEndings("\n"),
            await fixture.RenderAsync()
        );
    }

    [Fact]
    public async Task A_linked_generator_runs_in_capture_beside_one_the_bundle_lacks()
    {
        var fixture = new SourceGenerationFixture();
        var linked = new CompiledSourceGenerator(
            new ConstantSourceGenerator().AsSourceGenerator(),
            SourceGenerationFixture.Fingerprint
        );
        var missing = new CompiledSourceGenerator(
            new ConstantSourceGenerator().AsSourceGenerator(),
            new string('B', 64)
        );
        await fixture.ArrangeAsync([linked, missing]);
        var fallback = fixture.RespondAsync();

        var result = await fixture.RunAsync(new TestSourceGeneratorCatalog(linked));

        Assert.Equal(RuleExitCode.Findings, result);
        Assert.Equal("", fixture.Error.ToString());
        Assert.Empty((await fallback).References);
        Assert.Equal(
            """
            GEN001 Generated type is available.
            Use.cs
              1:14

            """.ReplaceLineEndings("\n"),
            await fixture.RenderAsync()
        );
    }

    [Fact]
    public async Task A_bundle_without_a_catalogue_runs_every_generator_in_capture()
    {
        var fixture = new SourceGenerationFixture();
        var generator = new CompiledSourceGenerator(
            new ConstantSourceGenerator().AsSourceGenerator(),
            SourceGenerationFixture.Fingerprint
        );
        await fixture.ArrangeAsync(generator);
        var fallback = fixture.RespondAsync();

        var result = await fixture.RunAsync(null);

        Assert.Equal(RuleExitCode.Findings, result);
        Assert.Equal("", fixture.Error.ToString());
        Assert.Empty((await fallback).References);
        Assert.Equal(
            """
            GEN001 Generated type is available.
            Use.cs
              1:14

            """.ReplaceLineEndings("\n"),
            await fixture.RenderAsync()
        );
    }

    [Fact]
    public async Task User_diagnostic_suppression_cannot_disable_internal_model_preparation()
    {
        var fixture = new SourceGenerationFixture();
        var generator = new CompiledSourceGenerator(
            new ConstantSourceGenerator().AsSourceGenerator(),
            SourceGenerationFixture.Fingerprint
        );
        var original = fixture.Snapshot.Projects.Single();
        var project = original with
        {
            CompilerOptions = original.CompilerOptions with
            {
                SpecificDiagnosticOptions = new() { ["DPGEN001"] = (int)ReportDiagnostic.Suppress },
            },
        };
        await fixture.ArrangeAsync(generator, project);

        var result = await fixture
            .RunAsync(new TestSourceGeneratorCatalog(generator))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(RuleExitCode.Findings, result);
        Assert.Equal("", fixture.Error.ToString());
        Assert.Equal(
            """
            GEN001 Generated type is available.
            Use.cs
              1:14

            """.ReplaceLineEndings("\n"),
            await fixture.RenderAsync()
        );
    }
}
