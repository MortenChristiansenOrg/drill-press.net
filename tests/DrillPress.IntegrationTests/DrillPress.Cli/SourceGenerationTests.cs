using System.Security.Cryptography;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

public sealed class SourceGenerationTests : IntegrationTest
{
    [Fact]
    public async Task Managed_fallback_receives_generated_dependency_types_and_matches_ordinary_capture()
    {
        var directory = CreateTemporaryDirectory("drillpress-generated-dependency-");
        await WriteAsync(
            directory.FullName,
            "Generators/Generators.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Reference Include="Microsoft.CodeAnalysis" HintPath="$(MSBuildToolsPath)/Roslyn/bincore/Microsoft.CodeAnalysis.dll" /></ItemGroup>
            </Project>
            """
        );
        await WriteAsync(
            directory.FullName,
            "Generators/Generators.cs",
            """
            using Microsoft.CodeAnalysis;
            namespace Fixture;
            [Generator]
            public sealed class DependencyGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context) =>
                    context.RegisterSourceOutput(context.CompilationProvider, static (output, compilation) =>
                    {
                        if (compilation.AssemblyName == "A") output.AddSource("GeneratedA.g.cs", "public class GeneratedA { }");
                    });
            }
            [Generator]
            public sealed class ConsumerGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context) =>
                    context.RegisterSourceOutput(context.CompilationProvider, static (output, compilation) =>
                    {
                        if (compilation.AssemblyName == "B" && compilation.GetTypeByMetadataName("GeneratedA") != null)
                            output.AddSource("GeneratedB.g.cs", "public class GeneratedB { }");
                    });
            }
            """
        );
        var generatorProject = FileSystem.Path.Combine(
            directory.FullName,
            "Generators/Generators.csproj"
        );
        var generatorBuild = await RunProcessAsync(
            "dotnet",
            ["build", generatorProject, "--nologo", "-v:q"],
            directory.FullName,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(0, generatorBuild.ExitCode);
        var generator = FileSystem.Path.Combine(
            directory.FullName,
            "Generators/bin/Debug/net10.0/Generators.dll"
        );
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(FileSystem.File.ReadAllBytes(generator))
        );
        await WriteAsync(
            directory.FullName,
            "A/A.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup><Analyzer Include="../Generators/bin/Debug/net10.0/Generators.dll" /></ItemGroup></Project>
            """
        );
        await WriteAsync(directory.FullName, "A/Anchor.cs", "public class Anchor { }");
        await WriteAsync(
            directory.FullName,
            "B/B.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup><ProjectReference Include="../A/A.csproj" /><Analyzer Include="../Generators/bin/Debug/net10.0/Generators.dll" /></ItemGroup></Project>
            """
        );
        await WriteAsync(directory.FullName, "B/Use.cs", "public class Use : GeneratedB { }");
        var consumer = FileSystem.Path.Combine(directory.FullName, "B/B.csproj");
        await RestoreAsync(consumer);
        var references = string.Join(
            "",
            new[]
            {
                "DrillPress.Engine",
                "DrillPress.RuleAuthoring",
                "DrillPress.Manifest",
                "Microsoft.CodeAnalysis",
                "Microsoft.CodeAnalysis.CSharp",
                "TestableIO.System.IO.Abstractions.Wrappers",
                "Testably.Abstractions.FileSystem.Interface",
            }.Select(name =>
                $"<Reference Include=\"{FileSystem.Path.Combine(AppContext.BaseDirectory, name + ".dll")}\" />"
            )
        );
        await WriteAsync(
            directory.FullName,
            "Rules/Rules.csproj",
            $$"""
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup>{{references}}<Reference Include="{{generator}}" /></ItemGroup></Project>
            """
        );
        await WriteAsync(
            directory.FullName,
            "Rules/Program.cs",
            $$"""
            using DrillPress;
            using DrillPress.Engine;
            using Microsoft.CodeAnalysis;
            [assembly: SourceGeneratorCatalogAttribute(typeof(Catalog))]
            var rules = new RuleCatalog();
            rules.Rule("GEN001", "Generated dependency was available.")
                .For(Code.Types.Named("Use").Where(type => type.Source.Project.HasType(CodeType.Named("GeneratedB")))).Forbid();
            return (int)await new RuleApplication().RunAsync(rules, args);
            public sealed class Catalog : SourceGeneratorCatalog
            {
                public Catalog() : base([new CompiledSourceGenerator(new Fixture.DependencyGenerator().AsSourceGenerator(), "{{fingerprint}}")]) { }
            }
            """
        );
        var rulesProject = FileSystem.Path.Combine(directory.FullName, "Rules/Rules.csproj");
        var ruleBuild = await RunProcessAsync(
            "dotnet",
            ["build", rulesProject, "--nologo", "-v:q"],
            directory.FullName,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(0, ruleBuild.ExitCode);
        var rulePath = FileSystem.Path.Combine(
            directory.FullName,
            "Rules/bin/Debug/net10.0/Rules.dll"
        );
        string[] command =
        [
            GetOutputPath("DrillPress.Cli"),
            "check",
            "--build-host",
            GetOutputPath("DrillPress.BuildHost"),
            "--rules",
            rulePath,
            consumer,
        ];

        var ordinary = await RunProcessAsync(
            "dotnet",
            command,
            directory.FullName,
            TestContext.Current.CancellationToken
        );
        var generated = await RunProcessAsync(
            "dotnet",
            [.. command, "--stream-compilation", "--parallel-queries"],
            directory.FullName,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            (
                1,
                "",
                """
                GEN001 Generated dependency was available.
                B/Use.cs
                  1:14

                """.ReplaceLineEndings("\n")
            ),
            (ordinary.ExitCode, ordinary.StandardError, ordinary.StandardOutput)
        );
        Assert.Equal(
            (ordinary.ExitCode, ordinary.StandardError, ordinary.StandardOutput),
            (generated.ExitCode, generated.StandardError, generated.StandardOutput)
        );
    }

    private static async Task WriteAsync(string directory, string path, string text)
    {
        var fullPath = FileSystem.Path.Combine(directory, path);
        FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(fullPath)!);
        await FileSystem.File.WriteAllTextAsync(
            fullPath,
            text,
            TestContext.Current.CancellationToken
        );
    }
}
