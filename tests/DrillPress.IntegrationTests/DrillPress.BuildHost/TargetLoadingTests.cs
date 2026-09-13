using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using DrillPress.SampleRules;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.BuildHost;

public sealed class TargetLoadingTests : IntegrationTest
{
    [Fact]
    public async Task Quoted_glob_is_ordinal_sorted_and_uses_loose_mode_inside_a_project()
    {
        var root = CreateTemporaryDirectory("drillpress-glob-").FullName;
        FileSystem.Directory.CreateDirectory(FileSystem.Path.Combine(root, "nested"));
        await WriteAsync(
            root,
            "Unrestored.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"
        );
        await WriteAsync(root, "Z.cs", "class Z { string Value => string.Empty; }");
        await WriteAsync(root, "nested/A.cs", "class A { }");
        var output = FileSystem.Path.Combine(root, "snapshot.json");

        var result = await ExportAsync(FileSystem.Path.Combine(root, "**", "?.cs"), output);
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            output,
            TestContext.Current.CancellationToken
        );
        var findings = await new AnalysisEngine().AnalyzeAsync(
            SampleRuleSet.Create(),
            snapshot,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new ProcessResult(0, "", ""), result);
        var project = Assert.Single(snapshot.Projects);
        Assert.Equal("LooseSource", project.Name);
        Assert.Equal(
            [
                FileSystem.Path.Combine(root, "Z.cs"),
                FileSystem.Path.Combine(root, "nested", "A.cs"),
            ],
            project.Documents.Select(document => document.Path)
        );
        Assert.All(project.Documents, document => Assert.True(document.IsEditable));
        Assert.Single(findings);
    }

    [Fact]
    public async Task Zero_match_glob_fails_without_creating_a_snapshot()
    {
        var root = CreateTemporaryDirectory("drillpress-zero-glob-").FullName;
        var target = FileSystem.Path.Combine(root, "**", "*.cs");
        var output = FileSystem.Path.Combine(root, "snapshot.json");

        var result = await ExportAsync(target, output);

        Assert.Equal(
            new ProcessResult(
                2,
                "",
                $"DrillPress.BuildHost: C# glob '{target}' matched no files.{Environment.NewLine}"
            ),
            result
        );
        Assert.False(FileSystem.File.Exists(output));
    }

    [Fact]
    public async Task Missing_restore_assets_fail_without_an_implicit_restore()
    {
        var root = CreateTemporaryDirectory("drillpress-missing-assets-").FullName;
        await WriteAsync(
            root,
            "Target.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"
        );
        await WriteAsync(root, "Source.cs", "class Target { }");
        var output = FileSystem.Path.Combine(root, "snapshot.json");

        var result = await ExportAsync(FileSystem.Path.Combine(root, "Target.csproj"), output);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Contains("dotnet restore", result.StandardError);
        Assert.False(
            FileSystem.File.Exists(FileSystem.Path.Combine(root, "obj", "project.assets.json"))
        );
        Assert.False(FileSystem.File.Exists(output));
    }

    [Fact]
    public async Task Fast_mode_preserves_an_erroneous_source_dependency_and_validation_rejects_it()
    {
        var root = CreateTemporaryDirectory("drillpress-erroneous-graph-").FullName;
        FileSystem.Directory.CreateDirectory(FileSystem.Path.Combine(root, "dependency"));
        FileSystem.Directory.CreateDirectory(FileSystem.Path.Combine(root, "consumer"));
        await WriteAsync(
            root,
            "dependency/Dependency.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"
        );
        await WriteAsync(
            root,
            "dependency/Source.cs",
            "public class Dependency { public static string Value => string.Empty; public Missing Error; }"
        );
        await WriteAsync(
            root,
            "consumer/Consumer.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>false</IsTestProject></PropertyGroup>
              <ItemGroup><ProjectReference Include="../dependency/Dependency.csproj" Aliases="dep" /></ItemGroup>
            </Project>
            """
        );
        await WriteAsync(
            root,
            "consumer/Source.cs",
            "extern alias dep; class Consumer { string Value => dep::Dependency.Value; }"
        );
        await WriteAsync(
            root,
            "Selected.slnx",
            "<Solution><Project Path=\"consumer/Consumer.csproj\" /></Solution>"
        );
        var target = FileSystem.Path.Combine(root, "Selected.slnx");
        await RestoreAsync(target);
        var output = FileSystem.Path.Combine(root, "fast.json");

        var fast = await ExportAsync(target, output);
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            output,
            TestContext.Current.CancellationToken
        );
        var contexts = new AnalysisEngine().Reconstruct(
            snapshot,
            TestContext.Current.CancellationToken
        );
        var validated = await ExportAsync(
            target,
            FileSystem.Path.Combine(root, "validated.json"),
            "--validate-compilation"
        );

        Assert.Equal(new ProcessResult(0, "", ""), fast);
        Assert.Equal(2, contexts.Length);
        var consumer = Assert.Single(contexts, context => context.Snapshot.Name == "Consumer");
        Assert.Equal(["dep"], Assert.Single(consumer.Snapshot.CompilationReferences).Aliases);
        Assert.Empty(
            consumer
                .Compilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(diagnostic =>
                    diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                )
        );
        Assert.Equal(2, validated.ExitCode);
        Assert.Contains("has 1 errors", validated.StandardError);
        Assert.False(FileSystem.File.Exists(FileSystem.Path.Combine(root, "validated.json")));
    }

    [Fact]
    public async Task Target_global_json_is_honored_from_an_unrelated_working_directory()
    {
        var root = CreateTemporaryDirectory("drillpress-global-json-").FullName;
        await WriteAsync(
            root,
            "global.json",
            """{"sdk":{"version":"99.0.100","rollForward":"disable"}}"""
        );
        await WriteAsync(
            root,
            "Target.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"
        );
        var output = FileSystem.Path.Combine(root, "snapshot.json");

        var result = await ExportAsync(FileSystem.Path.Combine(root, "Target.csproj"), output);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Contains("SDK", result.StandardError);
        Assert.False(FileSystem.File.Exists(output));
    }

    [Fact]
    public async Task Loose_source_preserves_utf16_bytes_and_bom()
    {
        var root = CreateTemporaryDirectory("drillpress-encoding-").FullName;
        var source = FileSystem.Path.Combine(root, "Source.cs");
        var text = "class Source { string Value => \"é\"; }\r\n";
        byte[] bytes =
        [
            .. System.Text.Encoding.Unicode.GetPreamble(),
            .. System.Text.Encoding.Unicode.GetBytes(text),
        ];
        await FileSystem.File.WriteAllBytesAsync(
            source,
            bytes,
            TestContext.Current.CancellationToken
        );
        var output = FileSystem.Path.Combine(root, "snapshot.json");

        var result = await ExportAsync(source, output);
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            output,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new ProcessResult(0, "", ""), result);
        var document = Assert.Single(Assert.Single(snapshot.Projects).Documents);
        Assert.Equal(text, document.Text);
        Assert.Equal("utf-16", document.EncodingName);
        Assert.True(document.HasByteOrderMark);
        Assert.True(document.IsEditable);
        Assert.Equal(bytes, SourceIdentity.Encode(document));
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("Obj")]
    [InlineData("OBJ")]
    public async Task Intermediate_directory_casing_does_not_create_rule_candidates(string name)
    {
        var root = CreateTemporaryDirectory("drillpress-generated-folder-").FullName;
        var generated = FileSystem
            .Directory.CreateDirectory(FileSystem.Path.Combine(root, name))
            .FullName;
        var source = FileSystem.Path.Combine(generated, "Input.cs");
        await FileSystem.File.WriteAllTextAsync(
            source,
            "class Input { string Value => string.Empty; }",
            TestContext.Current.CancellationToken
        );
        var output = FileSystem.Path.Combine(root, "snapshot.json");

        var result = await ExportAsync(source, output);
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            output,
            TestContext.Current.CancellationToken
        );
        var findings = await new AnalysisEngine().AnalyzeAsync(
            SampleRuleSet.Create(),
            snapshot,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new ProcessResult(0, "", ""), result);
        var document = Assert.Single(Assert.Single(snapshot.Projects).Documents);
        Assert.True(document.IsGenerated);
        Assert.False(document.IsEditable);
        Assert.Empty(findings);
    }

    private Task WriteAsync(string root, string path, string text) =>
        FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(root, path),
            text,
            TestContext.Current.CancellationToken
        );

    private Task<ProcessResult> ExportAsync(
        string target,
        string output,
        params string[] options
    ) =>
        RunProcessAsync(
            "dotnet",
            [GetOutputPath("DrillPress.BuildHost"), "export", target, output, .. options],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );
}
