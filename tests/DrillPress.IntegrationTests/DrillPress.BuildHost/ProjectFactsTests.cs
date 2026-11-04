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

    [Theory]
    [InlineData("Microsoft.NET.Sdk", "", "bin,bin/Debug,obj,obj/Debug", "Glob.cs,Nested/Deep.cs")]
    [InlineData(
        "Microsoft.NET.Sdk.Web",
        "",
        "bin,bin/Debug,obj,obj/Debug,wwwroot",
        "Glob.cs,Nested/Deep.cs"
    )]
    [InlineData(
        "Microsoft.NET.Sdk",
        "<ItemGroup><Compile Remove=\"Nested/**\" /></ItemGroup>",
        null,
        ""
    )]
    [InlineData(
        "Microsoft.NET.Sdk",
        "<PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include=\"**/*.cs\" /></ItemGroup>",
        null,
        ""
    )]
    [InlineData(
        "Microsoft.NET.Sdk",
        "<PropertyGroup><DefaultItemExcludes>$(DefaultItemExcludes);**/Legacy*.cs</DefaultItemExcludes></PropertyGroup>",
        null,
        ""
    )]
    public async Task Project_capture_records_the_default_compile_glob_only_when_nothing_else_selects_sources(
        string sdk,
        string customization,
        string? excluded,
        string globbed
    )
    {
        var root = CreateTemporaryDirectory("drillpress-compile-glob-").FullName;
        var target = FileSystem.Path.Combine(root, "Glob.csproj");
        var output = FileSystem.Path.Combine(root, "snapshot.json");
        FileSystem.File.WriteAllText(
            target,
            $"<Project Sdk=\"{sdk}\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType></PropertyGroup>{customization}</Project>"
        );
        FileSystem.File.WriteAllText(
            FileSystem.Path.Combine(root, "Glob.cs"),
            "public class Glob { }"
        );
        FileSystem.Directory.CreateDirectory(FileSystem.Path.Combine(root, "Nested"));
        FileSystem.File.WriteAllText(
            FileSystem.Path.Combine(root, "Nested", "Deep.cs"),
            "public class Deep { }"
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
            excluded?.Split(','),
            project
                .CompileGlob?.ExcludedDirectories.Select(path =>
                    FileSystem.Path.GetRelativePath(root, path).Replace('\\', '/')
                )
                .Order(StringComparer.Ordinal)
                .ToArray()
        );
        Assert.Equal(
            globbed,
            string.Join(
                ",",
                project
                    .Documents.Where(document => document.IsDefaultCompileItem)
                    .Select(document =>
                        FileSystem.Path.GetRelativePath(root, document.Path).Replace('\\', '/')
                    )
                    .Order(StringComparer.Ordinal)
            )
        );
    }
}
