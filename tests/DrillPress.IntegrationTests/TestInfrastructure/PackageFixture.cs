using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.TestInfrastructure;

public sealed class PackageFixture : IntegrationTest, IAsyncLifetime
{
    private readonly Dictionary<string, string> _environment = [];

    public string Root { get; private set; } = "";
    public string Feed { get; private set; } = "";
    public string Consumer { get; private set; } = "";
    public string ToolHome { get; private set; } = "";
    public string Config { get; private set; } = "";
    public string Version { get; } =
        typeof(CompilationSnapshot)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

    public async ValueTask InitializeAsync()
    {
        Root = CreateTemporaryDirectory("drillpress-packages-").FullName;
        Feed = FileSystem.Path.Combine(Root, "feed");
        Consumer = FileSystem.Path.Combine(Root, "consumer");
        ToolHome = FileSystem.Path.Combine(Root, "tool-home");
        FileSystem.Directory.CreateDirectory(Consumer);
        FileSystem.Directory.CreateDirectory(ToolHome);
        var releaseFeed = Environment.GetEnvironmentVariable("DRILLPRESS_RELEASE_FEED");
        if (string.IsNullOrEmpty(releaseFeed))
        {
            await PackCurrentReleaseAsync();
        }
        else
        {
            FileSystem.Directory.CreateDirectory(Feed);
            foreach (
                var package in FileSystem.Directory.GetFiles(
                    FileSystem.Path.GetFullPath(releaseFeed),
                    "*.nupkg"
                )
            )
            {
                FileSystem.File.Copy(
                    package,
                    FileSystem.Path.Combine(Feed, FileSystem.Path.GetFileName(package))
                );
            }
        }
        await ConfigureConsumerAsync();
        await InstallToolsAsync();
        await CreateRulesAsync();
    }

    private async Task PackCurrentReleaseAsync()
    {
        var configuration = typeof(PackageFixture)
            .Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!
            .Configuration;
        var pack = await RunProcessAsync(
            "dotnet",
            [
                "pack",
                RepositoryPath("DrillPress.slnx"),
                "-c",
                configuration,
                "-p:Version=" + Version,
                "--no-build",
                "--no-restore",
                "-o",
                Feed,
            ],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );
        Assert.True(pack.ExitCode == 0, pack.StandardOutput + pack.StandardError);
    }

    private async Task ConfigureConsumerAsync()
    {
        _environment["DOTNET_CLI_HOME"] = ToolHome;
        _environment["NUGET_PACKAGES"] = FileSystem.Path.Combine(Root, "packages");
        _environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        _environment["DOTNET_NOLOGO"] = "1";
        _environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        Config = FileSystem.Path.Combine(Consumer, "NuGet.Config");
        var config = new XElement(
            "configuration",
            new XElement(
                "packageSources",
                new XElement("clear"),
                new XElement(
                    "add",
                    new XAttribute("key", "distribution"),
                    new XAttribute("value", Feed)
                ),
                new XElement(
                    "add",
                    new XAttribute("key", "nuget.org"),
                    new XAttribute("value", "https://api.nuget.org/v3/index.json")
                )
            ),
            new XElement(
                "packageSourceMapping",
                new XElement("clear"),
                new XElement(
                    "packageSource",
                    new XAttribute("key", "distribution"),
                    new XElement("package", new XAttribute("pattern", "DrillPress.*"))
                ),
                new XElement(
                    "packageSource",
                    new XAttribute("key", "nuget.org"),
                    new XElement("package", new XAttribute("pattern", "*"))
                )
            )
        );
        await WriteAsync(Config, config.ToString());
    }

    private async Task InstallToolsAsync()
    {
        await RequireDotnetAsync(Consumer, "new", "tool-manifest", "-o", ".config");
        await RequireDotnetAsync(
            Consumer,
            "tool",
            "install",
            "DrillPress.Cli",
            "--local",
            "--version",
            Version,
            "--configfile",
            Config
        );
        await RequireDotnetAsync(
            Consumer,
            "tool",
            "install",
            "DrillPress.Cli",
            "--global",
            "--version",
            Version,
            "--configfile",
            Config
        );
    }

    private async Task CreateRulesAsync()
    {
        await WriteAsync(
            FileSystem.Path.Combine(Consumer, "Rules", "Rules.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="DrillPress.Engine" Version="[{{Version}}]" />
                <PackageReference Include="DrillPress.Testing" Version="[{{Version}}]" />
              </ItemGroup>
            </Project>
            """
        );
        await WriteAsync(
            FileSystem.Path.Combine(Consumer, "Rules", "Program.cs"),
            """
            using DrillPress;
            using DrillPress.Engine;
            using DrillPress.Semantics;
            using NullableFlowState = Microsoft.CodeAnalysis.NullableFlowState;
            using Microsoft.CodeAnalysis.CSharp.Syntax;

            var rules = new RuleSet();
            rules.For(OperationQueries.NullChecks.Where(check =>
                check.Condition.Syntax is IsPatternExpressionSyntax &&
                check.FlowStateBeforeCheck == NullableFlowState.NotNull))
                .Forbid("NULL001", "Unexpected non-null check.");
            rules.For(CodeType.Of<string>().Member(nameof(string.Empty)).References)
                .Forbid("EMPTY", "Use an empty literal.", fix: reference =>
                    SourceChanges.Propose(
                        [SourceChanges.Replace(reference.Source!, reference.Syntax!.Span, "\"\"")],
                        context => context.Original.Sources
                            .Where(source => source.Document.FileIdentity == reference.Source!.Document.FileIdentity)
                            .All(source => BindingProof.PreservesEnclosingExpressions(
                                source, source.Tree.GetRoot().FindNode(reference.Syntax!.Span, getInnermostNodeForTie: true), "\"\""))));
            return (int)await new RuleApplication().RunAsync(rules, args);
            """
        );
        await RequireDotnetAsync(Consumer, "build", "Rules/Rules.csproj", "-c", "Release");
    }

    public async Task<(int ExitCode, string StandardOutput, string StandardError)> DotnetAsync(
        string directory,
        params string[] arguments
    )
    {
        var result = await RunProcessAsync(
            "dotnet",
            arguments,
            directory,
            TestContext.Current.CancellationToken,
            _environment
        );
        return (result.ExitCode, result.StandardOutput, result.StandardError);
    }

    public async Task RequireDotnetAsync(string directory, params string[] arguments)
    {
        var result = await DotnetAsync(directory, arguments);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
    }

    public async Task<(int ExitCode, string StandardOutput, string StandardError)> GlobalAsync(
        params string[] arguments
    )
    {
        var result = await RunProcessAsync(
            FileSystem.Path.Combine(
                ToolHome,
                ".dotnet",
                "tools",
                OperatingSystem.IsWindows() ? "drillpress.exe" : "drillpress"
            ),
            arguments,
            Root,
            TestContext.Current.CancellationToken,
            _environment
        );
        return (result.ExitCode, result.StandardOutput, result.StandardError);
    }

    public async Task<string> CreateTargetAsync()
    {
        var target = FileSystem.Path.Combine(Consumer, "Target", "Target.csproj");
        await WriteAsync(
            target,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """
        );
        await WriteAsync(
            FileSystem.Path.Combine(Consumer, "Target", "Source.cs"),
            """
            public static class Example
            {
                public static string Value => string.Empty;
            }
            """
        );
        await RequireDotnetAsync(Consumer, "restore", target);
        return target;
    }

    public string Bundle =>
        FileSystem.Path.Combine(Consumer, "Rules", "bin", "Release", "net10.0", "Rules.dll");

    public async Task RestorePinnedToolAsync()
    {
        var path = FileSystem.Path.Combine(Consumer, ".config", "dotnet-tools.json");
        var manifest = await FileSystem.File.ReadAllTextAsync(
            path,
            TestContext.Current.CancellationToken
        );
        using var json = JsonDocument.Parse(manifest);
        Assert.Equal(
            Version,
            json.RootElement.GetProperty("tools")
                .GetProperty("drillpress.cli")
                .GetProperty("version")
                .GetString()
        );
        await RequireDotnetAsync(Consumer, "tool", "uninstall", "DrillPress.Cli", "--local");
        await WriteAsync(path, manifest);
        await RequireDotnetAsync(Consumer, "tool", "restore", "--configfile", Config);
    }

    public async Task<string> BuildIncompatibleBundleAsync()
    {
        var otherVersion = Version + "-incompatible";
        foreach (
            var id in new[]
            {
                "DrillPress.Manifest",
                "DrillPress.RuleAuthoring",
                "DrillPress.Engine",
                "DrillPress.Testing",
            }
        )
        {
            var pack = await RunProcessAsync(
                "dotnet",
                [
                    "pack",
                    RepositoryPath("src", id, id + ".csproj"),
                    "-c",
                    "Release",
                    "-p:Version=" + otherVersion,
                    "--artifacts-path",
                    FileSystem.Path.Combine(Root, "alternate-build"),
                    "-o",
                    Feed,
                ],
                RepositoryRoot,
                TestContext.Current.CancellationToken
            );
            Assert.True(pack.ExitCode == 0, pack.StandardOutput + pack.StandardError);
        }

        var project = await FileSystem.File.ReadAllTextAsync(
            FileSystem.Path.Combine(Consumer, "Rules", "Rules.csproj"),
            TestContext.Current.CancellationToken
        );
        var program = await FileSystem.File.ReadAllTextAsync(
            FileSystem.Path.Combine(Consumer, "Rules", "Program.cs"),
            TestContext.Current.CancellationToken
        );
        await WriteAsync(
            FileSystem.Path.Combine(Consumer, "OtherRules", "Rules.csproj"),
            project.Replace($"[{Version}]", $"[{otherVersion}]")
        );
        await WriteAsync(FileSystem.Path.Combine(Consumer, "OtherRules", "Program.cs"), program);
        await RequireDotnetAsync(Consumer, "build", "OtherRules/Rules.csproj", "-c", "Release");
        return FileSystem.Path.Combine(
            Consumer,
            "OtherRules",
            "bin",
            "Release",
            "net10.0",
            "Rules.dll"
        );
    }

    private static async Task WriteAsync(string path, string content)
    {
        FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(path)!);
        await FileSystem.File.WriteAllTextAsync(
            path,
            content.ReplaceLineEndings("\n"),
            TestContext.Current.CancellationToken
        );
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
