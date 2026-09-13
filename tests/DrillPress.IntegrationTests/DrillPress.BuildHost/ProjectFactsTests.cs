using System.Xml.Linq;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.BuildHost;

public sealed class ProjectFactsTests : IntegrationTest
{
    [Fact]
    public async Task Project_capture_preserves_direct_central_packages_and_policy_properties()
    {
        var root = CreateTemporaryDirectory("drillpress-project-facts-").FullName;
        var target = FileSystem.Path.Combine(root, "Facts.csproj");
        var output = FileSystem.Path.Combine(root, "snapshot.json");
        var packages = XDocument.Parse(
            FileSystem.File.ReadAllText(RepositoryPath("Directory.Packages.props"))
        );
        var version = packages
            .Descendants("PackageVersion")
            .Single(item => item.Attribute("Include")!.Value == "Microsoft.NET.StringTools")
            .Attribute("Version")!
            .Value;
        FileSystem.File.WriteAllText(
            FileSystem.Path.Combine(root, "Directory.Packages.props"),
            $$"""
            <Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
            <ItemGroup><PackageVersion Include="Microsoft.NET.StringTools" Version="{{version}}" /></ItemGroup></Project>
            """
        );
        FileSystem.File.WriteAllText(
            target,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><RootNamespace>Example.Contract</RootNamespace></PropertyGroup>
              <ItemGroup><PackageReference Include="Microsoft.NET.StringTools" /></ItemGroup>
              <ItemGroup><SourceRoot Include="$(MSBuildProjectDirectory)/z/;$(MSBuildProjectDirectory)/ä/;$(MSBuildProjectDirectory)/a/" /></ItemGroup>
            </Project>
            """
        );
        FileSystem.File.WriteAllText(
            FileSystem.Path.Combine(root, "Source.cs"),
            "public class Example { }"
        );
        await RestoreAsync(target);

        var exported = await RunProcessAsync(
            "dotnet",
            [GetOutputPath("DrillPress.BuildHost"), "export", target, output],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            output,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new ProcessResult(0, "", ""), exported);
        var project = Assert.Single(snapshot.Projects);
        Assert.Equal(
            [new PackageReferenceSnapshot("Microsoft.NET.StringTools", version)],
            project.Packages
        );
        Assert.Equal("Example.Contract", project.Properties["RootNamespace"]);
        Assert.Equal("enable", project.Properties["Nullable"]);
        Assert.Equal(
            [
                root.Replace('\\', '/') + "/a/",
                root.Replace('\\', '/') + "/z/",
                root.Replace('\\', '/') + "/ä/",
            ],
            project
                .SourceRoots.Select(path => path.Replace('\\', '/'))
                .Where(path =>
                    path.StartsWith(root.Replace('\\', '/') + "/", StringComparison.Ordinal)
                )
        );
        Assert.Equal(project.SourceRoots.Order(StringComparer.Ordinal), project.SourceRoots);
    }
}
