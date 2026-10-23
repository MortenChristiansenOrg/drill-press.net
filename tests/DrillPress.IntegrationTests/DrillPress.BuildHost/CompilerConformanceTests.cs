using System.Text.Json;
using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using DrillPress.SampleRules;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.BuildHost;

public sealed class CompilerConformanceTests(CompilerFixture fixture)
    : IntegrationTest,
        IClassFixture<CompilerFixture>
{
    private static readonly string AnalyzersPath = RepositoryPath(
        "fixtures",
        "CompilerSnapshot",
        "Analyzers",
        "bin",
        "Release",
        "netstandard2.0",
        "Analyzers.dll"
    );
    private static readonly string GeneratorPath = RepositoryPath(
        "fixtures",
        "CompilerSnapshot",
        "Generator",
        "bin",
        "Release",
        "netstandard2.0",
        "Generator.dll"
    );
    private readonly CompilerFixture _fixture = fixture;

    [Fact]
    public async Task All_framework_contexts_preserve_generation_aliases_linked_text_and_compiler_severity()
    {
        var report = FileSystem.Path.Combine(
            CreateTemporaryDirectory("drillpress-conformance-test-").FullName,
            "report.json"
        );

        var result = await RunProcessAsync(
            "dotnet",
            [GetOutputPath("DrillPress.Conformance", "tools"), _fixture.Target, report],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );
        using var json = JsonDocument.Parse(
            await FileSystem.File.ReadAllTextAsync(report, TestContext.Current.CancellationToken)
        );

        Assert.Equal(new ProcessResult(0, "", ""), result);
        Assert.True(json.RootElement.GetProperty("ruleParity").GetBoolean());
        Assert.Equal(
            ["Consumer(net10.0)", "Consumer(net9.0)", "Dependency(net10.0)", "Dependency(net9.0)"],
            json.RootElement.GetProperty("contexts")
                .EnumerateArray()
                .Select(context => context.GetProperty("Project").GetString())
        );
        Assert.All(
            json.RootElement.GetProperty("contexts").EnumerateArray(),
            context => Assert.True(context.GetProperty("Equal").GetBoolean())
        );
    }

    [Fact]
    public async Task Evaluated_properties_override_classification_and_select_one_framework_graph()
    {
        var output = FileSystem.Path.Combine(
            CreateTemporaryDirectory("drillpress-properties-").FullName,
            "snapshot.json"
        );

        var result = await ExportAsync(
            output,
            "--property",
            "TargetFramework=net10.0",
            "--property",
            "IsTestProject=false"
        );
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            output,
            TestContext.Current.CancellationToken
        );
        var contexts = new AnalysisEngine().Reconstruct(
            snapshot,
            TestContext.Current.CancellationToken
        );
        var findings = await new AnalysisEngine().AnalyzeAsync(
            SampleRuleCatalog.Create(),
            snapshot,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new ProcessResult(0, "", ""), result);
        Assert.Equal(["Consumer", "Dependency"], contexts.Select(context => context.Snapshot.Name));
        Assert.All(contexts, context => Assert.False(context.Snapshot.IsTestProject));
        Assert.All(contexts, context => Assert.Equal("net10.0", context.Snapshot.TargetFramework));
        var consumer = contexts[0];
        Assert.Equal([contexts[1].Snapshot.ContextId], consumer.Snapshot.ReferencedContextIds);
        Assert.NotNull(consumer.Compilation.GetTypeByMetadataName("Fixture.Generated"));
        var interop = Assert.Single(
            consumer.Snapshot.ExternalReferences,
            reference => reference.EmbedInteropTypes
        );
        Assert.Equal(["interop"], interop.Aliases);
        Assert.Contains(
            consumer.Compilation.References,
            reference =>
                reference.Properties.EmbedInteropTypes
                && reference.Properties.Aliases.SequenceEqual(["interop"])
        );
        Assert.Contains(
            consumer.Snapshot.Documents,
            document =>
                document.IsGenerated
                && document.Text.Contains("class Generated")
                && !document.IsEditable
        );
        Assert.DoesNotContain(
            findings,
            finding => finding.Location.FilePath.Contains("Generated.g.cs")
        );
        Assert.True(consumer.Compilation.Options.AllowUnsafe);
        Assert.True(consumer.Compilation.Options.CheckOverflow);
        Assert.Contains(
            consumer.Compilation.GetDiagnostics(TestContext.Current.CancellationToken),
            diagnostic =>
                diagnostic.Id == "CS0168"
                && diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
        );
    }

    [Fact]
    public async Task Classic_solution_and_its_directory_select_the_same_graph()
    {
        var root = CreateTemporaryDirectory("drillpress-classic-solution-").FullName;
        var target = FileSystem.Path.Combine(root, "Selected.sln");
        await FileSystem.File.WriteAllTextAsync(
            target,
            $$$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Consumer", "{{{_fixture.Consumer}}}", "{77F24E93-8B73-46CC-BD2E-7947806D6792}"
            EndProject
            Global
            EndGlobal
            """,
            TestContext.Current.CancellationToken
        );
        var first = FileSystem.Path.Combine(root, "first.json");
        var second = FileSystem.Path.Combine(root, "second.json");

        var solutionResult = await RunProcessAsync(
            "dotnet",
            [GetOutputPath("DrillPress.BuildHost"), "export", target, first],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );
        var directoryResult = await RunProcessAsync(
            "dotnet",
            [GetOutputPath("DrillPress.BuildHost"), "export", root, second],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );
        var solution = await new CompilationSnapshotFile().ReadAsync(
            first,
            TestContext.Current.CancellationToken
        );
        var directory = await new CompilationSnapshotFile().ReadAsync(
            second,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new ProcessResult(0, "", ""), solutionResult);
        Assert.Equal(new ProcessResult(0, "", ""), directoryResult);
        Assert.Equal(
            ["Consumer(net10.0)", "Consumer(net9.0)", "Dependency(net10.0)", "Dependency(net9.0)"],
            solution.Projects.Select(project => project.Name)
        );
        Assert.Equal(
            solution.Projects.Select(project => project.Name),
            directory.Projects.Select(project => project.Name)
        );
    }

    [Fact]
    public async Task Cli_forwards_properties_and_compiler_validation_before_rendering()
    {
        var arguments = new[]
        {
            GetOutputPath("DrillPress.Cli"),
            "check",
            "--build-host",
            GetOutputPath("DrillPress.BuildHost"),
            "--rules",
            GetOutputPath("DrillPress.SampleRules", "samples"),
            "--validate-compilation",
            "--property",
            "NoWarn=CS0168",
            "--property",
            "TargetFramework=net10.0",
            _fixture.Consumer,
        };

        var result = await RunProcessAsync(
            "dotnet",
            arguments,
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardError);
        Assert.Equal(
            """
            DP1004 Use the empty string literal "" instead of string.Empty.
            fixtures/CompilerSnapshot/Consumer/Source.cs
              +20:28
            fixtures/CompilerSnapshot/Linked.cs
              +6:35

            """.ReplaceLineEndings("\n"),
            result.StandardOutput
        );
    }

    public static TheoryData<string[]> GeneratorOptions =>
        new() { new string[] { }, new string[] { "--validate-compilation" } };

    [Theory]
    [MemberData(nameof(GeneratorOptions))]
    public async Task Generator_exceptions_fail_exports_with_the_first_context_in_snapshot_order(
        string[] options
    )
    {
        var output = FileSystem.Path.Combine(
            CreateTemporaryDirectory("drillpress-generator-failure-").FullName,
            "snapshot.json"
        );

        var result = await ExportAsync(output, ["--property", "FailGenerator=true", .. options]);

        Assert.Equal(
            new ProcessResult(
                2,
                "",
                $"DrillPress.BuildHost: Source generation failed in 'Consumer(net10.0)': Requested generator failure.{Environment.NewLine}"
            ),
            result
        );
        Assert.False(FileSystem.File.Exists(output));
    }

    [Fact]
    public async Task Generators_receive_additional_files_in_build_order()
    {
        var target = await CreateAnalyzedProjectAsync(
            "",
            "namespace Fixture; public interface IContract { string Value { get; } }",
            analyzer: GeneratorPath,
            items: """
            <AdditionalFiles Include="zeta.fixture" />
            <AdditionalFiles Include="alpha.fixture" />
            <AdditionalFiles Include="mid.fixture" />
            <AdditionalFiles Include="beta.fixture" />
            """
        );
        foreach (var name in new[] { "zeta", "alpha", "mid", "beta" })
            await FileSystem.File.WriteAllTextAsync(
                FileSystem.Path.Combine(
                    FileSystem.Path.GetDirectoryName(target)!,
                    $"{name}.fixture"
                ),
                name,
                TestContext.Current.CancellationToken
            );

        var result = await ExportAnalyzedAsync(target);
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            FileSystem.Path.Combine(FileSystem.Path.GetDirectoryName(target)!, "snapshot.json"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new ProcessResult(0, "", ""), result);
        Assert.Equal(
            ["zeta", "alpha", "mid", "beta"],
            snapshot
                .Projects.Single()
                .Documents.Where(document => document.Path.EndsWith(".fixture.g.cs"))
                .Select(document => document.Text)
                .Select(text => text["// ".Length..])
        );
    }

    // ASP.NET's validation generator treats any Map call as an endpoint and resolves a readonly field
    // passed last through the field's declaration, which only the field's own compilation can bind.
    // The fixture generator reports the declarations it sees, so the test holds if ASP.NET changes.
    [Fact]
    public async Task Generators_see_referenced_projects_as_assemblies_like_the_compiler()
    {
        var root = CreateTemporaryDirectory("drillpress-generator-references-").FullName;
        var dependency = FileSystem.Path.Combine(root, "Dependency", "Dependency.csproj");
        var consumer = FileSystem.Path.Combine(root, "Consumer", "Consumer.csproj");
        FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(dependency)!);
        FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(consumer)!);
        await FileSystem.File.WriteAllTextAsync(
            dependency,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(root, "Dependency", "Clock.cs"),
            "public static class Clock { public static readonly System.DateTime Now = default; }",
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            consumer,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
                <Analyzer Include="{GeneratorPath}" />
                <ProjectReference Include="../Dependency/Dependency.csproj" />
              </ItemGroup>
            </Project>
            """,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(root, "Consumer", "Mapper.cs"),
            """
            namespace Fixture
            {
                public interface IContract { string Value { get; } }
            }

            public static class Mapper
            {
                public static System.DateTime Map(System.DateTime value) => value;

                public static System.DateTime Today() => Mapper.Map(Clock.Now);
            }
            """,
            TestContext.Current.CancellationToken
        );
        await RestoreAsync(consumer);

        var result = await ValidateAsync(consumer);
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            FileSystem.Path.Combine(root, "Consumer", "snapshot.json"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new ProcessResult(0, "", ""), result);
        Assert.Equal(
            "// Clock.Now has 0 source declarations",
            snapshot
                .Projects.Single(project => project.Name == "Consumer")
                .Documents.Single(document => document.Path.EndsWith("Clock.g.cs"))
                .Text
        );
    }

    [Fact]
    public async Task Validation_applies_analyzer_suppressors_and_honors_warning_exemptions()
    {
        var target = await CreateAnalyzedProjectAsync(
            """
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
            <WarningsNotAsErrors>CS8032</WarningsNotAsErrors>
            """,
            """
            public sealed class FixtureSet<T> { }
            public sealed class Context
            {
                public FixtureSet<string> Items { get; set; }
                public string Name { get; set; }
            }
            """
        );

        var result = await ValidateAsync(target);

        Assert.Equal(
            new ProcessResult(
                2,
                "",
                $"DrillPress.BuildHost: Compilation 'Analyzed' has 1 errors: {FileSystem.Path.Combine(FileSystem.Path.GetDirectoryName(target)!, "Context.cs")}(5,19): error CS8618: Non-nullable property 'Name' must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring the property as nullable.{Environment.NewLine}"
            ),
            result
        );
    }

    [Fact]
    public async Task Validation_accepts_a_project_whose_only_errors_are_suppressed()
    {
        var target = await CreateAnalyzedProjectAsync(
            """
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
            <WarningsNotAsErrors>CS8032</WarningsNotAsErrors>
            """,
            """
            public sealed class FixtureSet<T> { }
            public sealed class Context
            {
                public FixtureSet<string> Items { get; set; }
            }
            """
        );

        var result = await ValidateAsync(target);

        Assert.Equal(new ProcessResult(0, "", ""), result);
        Assert.True(
            FileSystem.File.Exists(
                FileSystem.Path.Combine(FileSystem.Path.GetDirectoryName(target)!, "snapshot.json")
            )
        );
    }

    [Fact]
    public async Task Validation_reports_suppressor_exceptions_configured_as_errors()
    {
        var target = await CreateAnalyzedProjectAsync(
            "<WarningsAsErrors>AD0001</WarningsAsErrors>",
            """
            public sealed class ThrowingSet<T> { }
            public sealed class Context
            {
                public ThrowingSet<string> Values { get; set; }
            }
            """
        );

        var result = await ValidateAsync(target);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.StartsWith(
            "DrillPress.BuildHost: Compilation 'Analyzed' has 1 errors: error AD0001: Analyzer 'CompilerSnapshot.Fixtures.FixtureSetSuppressor' threw an exception of type 'System.InvalidOperationException' with message 'Requested suppressor failure.'.",
            result.StandardError
        );
    }

    [Fact]
    public async Task Validation_reports_analyzer_load_failures_configured_as_errors()
    {
        var target = await CreateAnalyzedProjectAsync(
            "",
            "public sealed class Context { }",
            """
            is_global = true
            dotnet_diagnostic.CS8032.severity = error
            """
        );

        var result = await ValidateAsync(target);

        Assert.Equal(
            new ProcessResult(
                2,
                "",
                $"DrillPress.BuildHost: Compilation 'Analyzed' has 1 errors: error CS8032: An instance of analyzer CompilerSnapshot.Fixtures.UnconstructibleAnalyzer cannot be created from {AnalyzersPath}: Exception has been thrown by the target of an invocation.{Environment.NewLine}"
            ),
            result
        );
    }

    [Fact]
    public async Task Analyzers_referencing_a_newer_compiler_are_skipped_like_the_real_build()
    {
        var generator = await CreateFutureGeneratorAsync();
        var target = await CreateAnalyzedProjectAsync(
            "",
            "public sealed class Context { }",
            analyzer: generator
        );

        var result = await ExportAnalyzedAsync(target);
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            FileSystem.Path.Combine(FileSystem.Path.GetDirectoryName(target)!, "snapshot.json"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new ProcessResult(0, "", ""), result);
        Assert.Equal(
            [
                "Context.cs",
                ".NETCoreApp,Version=v10.0.AssemblyAttributes.cs",
                "Analyzed.AssemblyInfo.cs",
            ],
            Assert
                .Single(snapshot.Projects)
                .Documents.Select(document => FileSystem.Path.GetFileName(document.Path))
        );
    }

    [Fact]
    public async Task Validation_reports_newer_compiler_analyzers_configured_as_errors()
    {
        var generator = await CreateFutureGeneratorAsync();
        var target = await CreateAnalyzedProjectAsync(
            "<WarningsAsErrors>CS9057</WarningsAsErrors>",
            "public sealed class Context { }",
            analyzer: generator
        );

        var result = await ValidateAsync(target);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.StartsWith(
            $"DrillPress.BuildHost: Compilation 'Analyzed' has 1 errors: error CS9057: Analyzer assembly '{generator}' cannot be used because it references version '99.0.0.0' of the compiler, which is newer than the currently running version '",
            result.StandardError
        );
    }

    [Theory]
    [InlineData("[Microsoft.CodeAnalysis.Generator(\"c#\")] public sealed class G { }")]
    [InlineData(
        """
            namespace Microsoft.CodeAnalysis { public sealed class GeneratorAttribute : System.Attribute { } }
            [Microsoft.CodeAnalysis.Generator] public sealed class G { }
            [Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer("Visual Basic")] public sealed class B { }
            """
    )]
    public async Task Csharp_analyzers_are_judged_regardless_of_attribute_form(string source)
    {
        var analyzer = await CreateAnalyzerAssemblyAsync("99.0.0.0", source);
        var target = await CreateAnalyzedProjectAsync(
            "",
            "public sealed class Context { }",
            analyzer: analyzer
        );

        var result = await ExportAnalyzedAsync(target);

        Assert.Equal(new ProcessResult(0, "", ""), result);
    }

    [Fact]
    public async Task Assemblies_without_csharp_analyzers_are_not_judged_by_compiler_version()
    {
        var analyzer = await CreateAnalyzerAssemblyAsync(
            "99.0.0.0",
            """
            [Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer("Visual Basic")]
            public sealed class BasicAnalyzer { }
            [Microsoft.CodeAnalysis.Generator("C#", null)]
            public sealed class IgnoredGenerator { }
            public sealed class Helper { }
            """
        );
        var target = await CreateAnalyzedProjectAsync(
            "<WarningsAsErrors>CS9057</WarningsAsErrors>",
            "public sealed class Context { }",
            analyzer: analyzer
        );

        var result = await ValidateAsync(target);

        Assert.Equal(new ProcessResult(0, "", ""), result);
    }

    // The overridden compiler accepts the generator that the SDK compiler would skip, so the
    // BuildHost's own Roslyn becomes the limit.
    [Theory]
    [InlineData("CscToolPath", "")]
    [InlineData("CscToolExe", "csc.dll")]
    public async Task Compiler_overrides_can_require_a_drill_press_update(
        string property,
        string file
    )
    {
        var compiler = await CreateStubCompilerAsync("100.0.0.0");
        var generator = await CreateFutureGeneratorAsync();
        var target = await CreateAnalyzedProjectAsync(
            $"<{property}>{FileSystem.Path.Combine(compiler, file)}</{property}>",
            "public sealed class Context { }",
            analyzer: generator
        );

        var result = await ExportAnalyzedAsync(target);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.StartsWith(
            $"DrillPress.BuildHost: Analyzer loading failed in 'Analyzed': Analyzer assembly '{generator}' references compiler version '99.0.0.0', which is newer than Drill Press's Roslyn '",
            result.StandardError
        );
    }

    [Fact]
    public async Task Unreadable_compiler_metadata_preserves_analyzer_load_failure_handling()
    {
        var compiler = await CreateStubCompilerAsync("100.0.0.0");
        var generator = await CreateFutureGeneratorAsync();
        var target = await CreateAnalyzedProjectAsync(
            $"<CscToolPath>{compiler}</CscToolPath>",
            "public sealed class Context { }",
            analyzer: generator
        );
        using var locked = FileSystem.File.Open(
            FileSystem.Path.Combine(compiler, "Microsoft.CodeAnalysis.dll"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );
        var hostVersion = typeof(Microsoft.CodeAnalysis.Diagnostics.AnalyzerFileReference)
            .Assembly.GetName()
            .Version;

        var result = await ExportAnalyzedAsync(target);

        Assert.Equal(
            new ProcessResult(
                2,
                "",
                $"DrillPress.BuildHost: Analyzer loading failed in 'Analyzed': Analyzer assembly '{generator}' references compiler version '99.0.0.0', which is newer than Drill Press's Roslyn '{hostVersion}'. Update Drill Press.{Environment.NewLine}"
            ),
            result
        );
    }

    private async Task<string> CreateAnalyzedProjectAsync(
        string properties,
        string source,
        string globalConfig = "",
        string? analyzer = null,
        string items = ""
    )
    {
        var root = CreateTemporaryDirectory("drillpress-analyzers-").FullName;
        var target = FileSystem.Path.Combine(root, "Analyzed.csproj");
        await FileSystem.File.WriteAllTextAsync(
            target,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                {properties}
                <IsTestProject>false</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <Analyzer Include="{analyzer ?? AnalyzersPath}" />
                <GlobalAnalyzerConfigFiles Include="fixture.globalconfig" />
                {items}
              </ItemGroup>
            </Project>
            """,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(root, "Context.cs"),
            source,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(root, "fixture.globalconfig"),
            globalConfig,
            TestContext.Current.CancellationToken
        );
        await RestoreAsync(target);
        return target;
    }

    // A generator referencing a compiler newer than any SDK ships, independent of the installed SDK.
    private Task<string> CreateFutureGeneratorAsync() =>
        CreateAnalyzerAssemblyAsync(
            "99.0.0.0",
            "[Microsoft.CodeAnalysis.Generator] public sealed class FutureGenerator { }"
        );

    private async Task<string> CreateAnalyzerAssemblyAsync(string compilerVersion, string source)
    {
        var path = FileSystem.Path.Combine(
            CreateTemporaryDirectory("drillpress-analyzer-assembly-").FullName,
            "FixtureAnalyzer.dll"
        );
        var runtime = await RuntimeReferenceAsync();
        await FileSystem.File.WriteAllBytesAsync(
            path,
            Emit(
                Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                    "FixtureAnalyzer",
                    [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)],
                    [
                        runtime,
                        Microsoft.CodeAnalysis.MetadataReference.CreateFromImage(
                            EmitCompiler(compilerVersion, runtime)
                        ),
                    ],
                    new(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary)
                )
            ),
            TestContext.Current.CancellationToken
        );
        return path;
    }

    private async Task<string> CreateStubCompilerAsync(string version)
    {
        var directory = CreateTemporaryDirectory("drillpress-compiler-").FullName;
        // Design-time builds only check that the compiler executable exists.
        foreach (var executable in new[] { "csc", "csc.exe", "csc.dll" })
            await FileSystem.File.WriteAllTextAsync(
                FileSystem.Path.Combine(directory, executable),
                "",
                TestContext.Current.CancellationToken
            );
        await FileSystem.File.WriteAllBytesAsync(
            FileSystem.Path.Combine(directory, "Microsoft.CodeAnalysis.dll"),
            EmitCompiler(version, await RuntimeReferenceAsync()),
            TestContext.Current.CancellationToken
        );
        return directory;
    }

    private async Task<Microsoft.CodeAnalysis.MetadataReference> RuntimeReferenceAsync() =>
        Microsoft.CodeAnalysis.MetadataReference.CreateFromImage(
            await FileSystem.File.ReadAllBytesAsync(
                typeof(object).Assembly.Location,
                TestContext.Current.CancellationToken
            )
        );

    // Declares only the identity and attributes the compiler inspects before loading analyzers.
    private static byte[] EmitCompiler(
        string version,
        Microsoft.CodeAnalysis.MetadataReference runtime
    ) =>
        Emit(
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                "Microsoft.CodeAnalysis",
                [
                    Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                        $$"""
                        [assembly: System.Reflection.AssemblyVersion("{{version}}")]
                        namespace Microsoft.CodeAnalysis
                        {
                            public sealed class GeneratorAttribute : System.Attribute
                            {
                                public GeneratorAttribute() { }
                                public GeneratorAttribute(string first, params string[] rest) { }
                            }
                        }
                        namespace Microsoft.CodeAnalysis.Diagnostics
                        {
                            public sealed class DiagnosticAnalyzerAttribute : System.Attribute
                            {
                                public DiagnosticAnalyzerAttribute(string first, params string[] rest) { }
                            }
                        }
                        """
                    ),
                ],
                [runtime],
                new(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary)
            )
        );

    private static byte[] Emit(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation)
    {
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return image.ToArray();
    }

    private Task<ProcessResult> ValidateAsync(string target) =>
        ExportAnalyzedAsync(target, "--validate-compilation");

    private Task<ProcessResult> ExportAnalyzedAsync(string target, params string[] options) =>
        RunProcessAsync(
            "dotnet",
            [
                GetOutputPath("DrillPress.BuildHost"),
                "export",
                target,
                FileSystem.Path.Combine(FileSystem.Path.GetDirectoryName(target)!, "snapshot.json"),
                .. options,
            ],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );

    private Task<ProcessResult> ExportAsync(string output, params string[] options) =>
        RunProcessAsync(
            "dotnet",
            [
                GetOutputPath("DrillPress.BuildHost"),
                "export",
                _fixture.Consumer,
                output,
                .. options,
            ],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );
}
