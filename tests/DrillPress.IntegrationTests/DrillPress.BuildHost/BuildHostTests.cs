using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using DrillPress.SampleRules;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.BuildHost;

public sealed class BuildHostTests : IntegrationTest
{
    [Fact]
    public async Task Preserves_member_binding_from_an_unbuilt_project_reference()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory("drillpress-project-reference-");
        var dependency = FileSystem.Directory.CreateDirectory(FileSystem.Path.Combine(directory.FullName, "Dependency"));
        var consumer = FileSystem.Directory.CreateDirectory(FileSystem.Path.Combine(directory.FullName, "Consumer"));
        await FileSystem.File.WriteAllTextAsync(FileSystem.Path.Combine(dependency.FullName, "Dependency.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """, cancellationToken);
        await FileSystem.File.WriteAllTextAsync(FileSystem.Path.Combine(dependency.FullName, "Target.cs"),
            "namespace Dependency; public static class Target { public static string Empty => \"\"; }",
            cancellationToken);
        var projectPath = FileSystem.Path.Combine(consumer.FullName, "Consumer.csproj");
        await FileSystem.File.WriteAllTextAsync(projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="../Dependency/Dependency.csproj" /></ItemGroup>
            </Project>
            """, cancellationToken);
        var sourcePath = FileSystem.Path.Combine(consumer.FullName, "Values.cs");
        await FileSystem.File.WriteAllTextAsync(sourcePath,
            "public static class Values { public static string Value => Dependency.Target.Empty; }",
            cancellationToken);
        var snapshotPath = FileSystem.Path.Combine(directory.FullName, "snapshot.json");
        var rules = new RuleSet();
        rules.For(Code.MemberReferences.Where(new RuleCondition<MemberReference>(reference =>
                reference.ContainingType == CodeType.Named("Dependency.Target") && reference.MemberName == "Empty")))
            .Forbid("TEST001", "Do not use Dependency.Target.Empty.");

        var result = await RunProcessAsync(
            "dotnet", [GetOutputPath("DrillPress.BuildHost"), "export", projectPath, snapshotPath],
            RepositoryRoot, cancellationToken);
        var snapshot = await new CompilationSnapshotFile().ReadAsync(snapshotPath, cancellationToken);
        var diagnostics = await new AnalysisEngine().AnalyzeAsync(rules, snapshot, cancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TEST001", diagnostic.Descriptor.Id);
        Assert.Equal(sourcePath, diagnostic.Location.FilePath);
        Assert.Equal(1, diagnostic.Location.Line);
        Assert.Equal(60, diagnostic.Location.Column);
        Assert.Empty(FileSystem.Directory.EnumerateFiles(dependency.FullName, "Dependency.dll", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Exports_the_sample_project_to_the_requested_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var buildHost = GetOutputPath("DrillPress.BuildHost");
        var temporaryDirectory = CreateTemporaryDirectory("drillpress-buildhost-");
        var snapshotPath = FileSystem.Path.Combine(temporaryDirectory.FullName, "requested.snapshot.json");

        var result = await RunProcessAsync(
            "dotnet",
            [buildHost, "export", SampleProjectPath, snapshotPath],
            RepositoryRoot,
            cancellationToken);
        var snapshot = await new CompilationSnapshotFile().ReadAsync(snapshotPath, cancellationToken);
        var diagnostics = await new AnalysisEngine().AnalyzeAsync(
            SampleRuleSet.Create(), snapshot, cancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        Assert.True(FileSystem.File.Exists(snapshotPath));
        var project = Assert.Single(snapshot.Projects);
        Assert.Equal("WidgetLibrary", project.Name);
        Assert.Equal(SampleProjectPath, project.ProjectPath);
        Assert.Contains(project.Documents, document =>
            document.Path.EndsWith("Contracts.cs") && !document.IsGenerated);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DP1004", diagnostic.Descriptor.Id);
        Assert.EndsWith("Contracts.cs", diagnostic.Location.FilePath);
        Assert.Equal(10, diagnostic.Location.Line);
        Assert.Equal(29, diagnostic.Location.Column);
    }
}
