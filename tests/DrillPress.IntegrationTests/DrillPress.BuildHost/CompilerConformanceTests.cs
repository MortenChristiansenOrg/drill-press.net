using System.Text.Json;
using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using DrillPress.SampleRules;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.BuildHost;

public sealed class CompilerConformanceTests(CompilerFixture fixture) : IntegrationTest, IClassFixture<CompilerFixture>
{
    private readonly CompilerFixture _fixture = fixture;

    [Fact]
    public async Task All_framework_contexts_preserve_generation_aliases_linked_text_and_compiler_severity()
    {
        var report = FileSystem.Path.Combine(CreateTemporaryDirectory("drillpress-conformance-test-").FullName, "report.json");

        var result = await RunProcessAsync("dotnet", [GetOutputPath("DrillPress.Conformance", "tools"), _fixture.Target, report],
            RepositoryRoot, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await FileSystem.File.ReadAllTextAsync(report, TestContext.Current.CancellationToken));

        Assert.Equal(new ProcessResult(0, "", ""), result);
        Assert.True(json.RootElement.GetProperty("ruleParity").GetBoolean());
        Assert.Equal(["Consumer(net10.0)", "Consumer(net9.0)", "Dependency(net10.0)", "Dependency(net9.0)"],
            json.RootElement.GetProperty("contexts").EnumerateArray().Select(context => context.GetProperty("Project").GetString()));
        Assert.All(json.RootElement.GetProperty("contexts").EnumerateArray(), context => Assert.True(context.GetProperty("Equal").GetBoolean()));
    }

    [Fact]
    public async Task Evaluated_properties_override_classification_and_select_one_framework_graph()
    {
        var output = FileSystem.Path.Combine(CreateTemporaryDirectory("drillpress-properties-").FullName, "snapshot.json");

        var result = await ExportAsync(output, "--property", "TargetFramework=net10.0", "--property", "IsTestProject=false");
        var snapshot = await new CompilationSnapshotFile().ReadAsync(output, TestContext.Current.CancellationToken);
        var contexts = new AnalysisEngine().Reconstruct(snapshot, TestContext.Current.CancellationToken);
        var findings = await new AnalysisEngine().AnalyzeAsync(SampleRuleSet.Create(), snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(new ProcessResult(0, "", ""), result);
        Assert.Equal(["Consumer", "Dependency"], contexts.Select(context => context.Snapshot.Name));
        Assert.All(contexts, context => Assert.False(context.Snapshot.IsTestProject));
        Assert.All(contexts, context => Assert.Equal("net10.0", context.Snapshot.TargetFramework));
        var consumer = contexts[0];
        Assert.Equal([contexts[1].Snapshot.ContextId], consumer.Snapshot.ReferencedContextIds);
        Assert.NotNull(consumer.Compilation.GetTypeByMetadataName("Fixture.Generated"));
        var interop = Assert.Single(consumer.Snapshot.ExternalReferences, reference => reference.EmbedInteropTypes);
        Assert.Equal(["interop"], interop.Aliases);
        Assert.Contains(consumer.Compilation.References, reference => reference.Properties.EmbedInteropTypes && reference.Properties.Aliases.SequenceEqual(["interop"]));
        Assert.Contains(consumer.Snapshot.Documents, document => document.IsGenerated && document.Text.Contains("class Generated") && !document.IsEditable);
        Assert.DoesNotContain(findings, finding => finding.Location.FilePath.Contains("Generated.g.cs"));
        Assert.True(consumer.Compilation.Options.AllowUnsafe);
        Assert.True(consumer.Compilation.Options.CheckOverflow);
        Assert.Contains(consumer.Compilation.GetDiagnostics(TestContext.Current.CancellationToken), diagnostic => diagnostic.Id == "CS0168" && diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }


    [Fact]
    public async Task Classic_solution_and_its_directory_select_the_same_graph()
    {
        var root = CreateTemporaryDirectory("drillpress-classic-solution-").FullName;
        var target = FileSystem.Path.Combine(root, "Selected.sln");
        await FileSystem.File.WriteAllTextAsync(target, $$$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Consumer", "{{{_fixture.Consumer}}}", "{77F24E93-8B73-46CC-BD2E-7947806D6792}"
            EndProject
            Global
            EndGlobal
            """, TestContext.Current.CancellationToken);
        var first = FileSystem.Path.Combine(root, "first.json");
        var second = FileSystem.Path.Combine(root, "second.json");

        var solutionResult = await RunProcessAsync("dotnet", [GetOutputPath("DrillPress.BuildHost"), "export", target, first], RepositoryRoot, TestContext.Current.CancellationToken);
        var directoryResult = await RunProcessAsync("dotnet", [GetOutputPath("DrillPress.BuildHost"), "export", root, second], RepositoryRoot, TestContext.Current.CancellationToken);
        var solution = await new CompilationSnapshotFile().ReadAsync(first, TestContext.Current.CancellationToken);
        var directory = await new CompilationSnapshotFile().ReadAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal(new ProcessResult(0, "", ""), solutionResult);
        Assert.Equal(new ProcessResult(0, "", ""), directoryResult);
        Assert.Equal(["Consumer(net10.0)", "Consumer(net9.0)", "Dependency(net10.0)", "Dependency(net9.0)"], solution.Projects.Select(project => project.Name));
        Assert.Equal(solution.Projects.Select(project => project.Name), directory.Projects.Select(project => project.Name));
    }

    [Fact]
    public async Task Cli_forwards_properties_and_compiler_validation_before_rendering()
    {
        var arguments = new[] { GetOutputPath("DrillPress.Cli"), "check", "--build-host", GetOutputPath("DrillPress.BuildHost"),
            "--rules", GetOutputPath("DrillPress.SampleRules", "samples"), "--validate-compilation",
            "--property", "NoWarn=CS0168", "--property", "TargetFramework=net10.0", _fixture.Consumer };

        var result = await RunProcessAsync("dotnet", arguments, RepositoryRoot, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardError);
        Assert.Equal("""
            DP1004 Use the empty string literal "" instead of string.Empty.
            fixtures/CompilerSnapshot/Consumer/Source.cs
              11:28
            fixtures/CompilerSnapshot/Linked.cs
              5:35

            """.ReplaceLineEndings("\n"), result.StandardOutput);
    }

    public static TheoryData<string[]> GeneratorOptions => new() { new string[] { }, new string[] { "--validate-compilation" } };

    [Theory]
    [MemberData(nameof(GeneratorOptions))]
    public async Task Generator_exceptions_fail_fast_and_validated_exports(string[] options)
    {
        var output = FileSystem.Path.Combine(CreateTemporaryDirectory("drillpress-generator-failure-").FullName, "snapshot.json");

        var result = await ExportAsync(output, ["--property", "FailGenerator=true", .. options]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Contains("Requested generator failure.", result.StandardError);
        Assert.False(FileSystem.File.Exists(output));
    }

    private Task<ProcessResult> ExportAsync(string output, params string[] options) =>
        RunProcessAsync("dotnet", [GetOutputPath("DrillPress.BuildHost"), "export", _fixture.Consumer, output, .. options],
            RepositoryRoot, TestContext.Current.CancellationToken);
}
