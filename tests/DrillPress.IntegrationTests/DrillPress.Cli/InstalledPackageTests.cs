using System.IO.Compression;
using System.Xml.Linq;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

[Collection(typeof(PackageCollection))]
public sealed class InstalledPackageTests(PackageFixture fixture) : IntegrationTest
{
    [Theory]
    [InlineData("string?", 0, "")]
    [InlineData(
        "string",
        1,
        """
            NULL001 Unexpected non-null check.
            nullable.cs
              6:13

            """
    )]
    public async Task Installed_bundle_reads_incoming_nullable_state_in_standalone_source(
        string parameterType,
        int exitCode,
        string output
    )
    {
        var target = FileSystem.Path.Combine(fixture.Root, "nullable.cs");
        await FileSystem.File.WriteAllTextAsync(
            target,
            $$"""
            #nullable enable
            class Example
            {
                void Check({{parameterType}} value)
                {
                    if (value is null)
                        return;
                }
            }
            """,
            TestContext.Current.CancellationToken
        );

        var result = await fixture.GlobalAsync("check", "--rules", fixture.Bundle, target);

        Assert.Equal((exitCode, output.ReplaceLineEndings("\n"), ""), result);
    }

    [Fact]
    public async Task Installed_packages_build_rules_check_fix_and_recheck_an_external_project()
    {
        var target = await fixture.CreateTargetAsync();
        var nested = FileSystem.Path.Combine(fixture.Consumer, "Rules");

        var check = await fixture.DotnetAsync(
            nested,
            "tool",
            "run",
            "drillpress",
            "--",
            "check",
            "--rules",
            fixture.Bundle,
            target
        );
        var fix = await fixture.DotnetAsync(
            nested,
            "tool",
            "run",
            "drillpress",
            "--",
            "fix",
            "--rules",
            fixture.Bundle,
            target
        );
        var clean = await fixture.GlobalAsync("check", "--rules", fixture.Bundle, target);
        var build = await fixture.DotnetAsync(fixture.Consumer, "build", target, "--no-restore");

        Assert.Equal(1, check.ExitCode);
        Assert.Equal("", check.StandardError);
        Assert.Equal(
            """
            EMPTY Use an empty literal.
            ../Target/Source.cs
              +3:35

            """.ReplaceLineEndings("\n"),
            check.StandardOutput
        );
        Assert.Equal((0, "", ""), fix);
        Assert.Equal((0, "", ""), clean);
        Assert.Equal(0, build.ExitCode);
        Assert.Equal(
            """
            public static class Example
            {
                public static string Value => "";
            }
            """.ReplaceLineEndings("\n"),
            await FileSystem.File.ReadAllTextAsync(
                FileSystem.Path.Combine(fixture.Consumer, "Target", "Source.cs"),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task Local_manifest_restoration_and_global_installation_identify_the_same_release()
    {
        await fixture.RestorePinnedToolAsync();

        var local = await fixture.DotnetAsync(
            fixture.Consumer,
            "tool",
            "run",
            "drillpress",
            "--",
            "--version"
        );
        var global = await fixture.GlobalAsync("--version");

        Assert.Equal(
            (
                0,
                $"drillpress {fixture.Version} (snapshot {CompilationSnapshot.CurrentFormatVersion}, response {BundleResponseProtocol.CurrentVersion})\n",
                ""
            ),
            local
        );
        Assert.Equal(local, global);
    }

    [Fact]
    public async Task Installed_tool_rejects_a_bundle_built_from_another_prerelease_suffix()
    {
        var bundle = await fixture.BuildIncompatibleBundleAsync();
        var target = await fixture.CreateTargetAsync();

        var result = await fixture.GlobalAsync("fix", "--rules", bundle, target);

        Assert.Equal(
            (
                2,
                "",
                $"drillpress-rules: Snapshot producer version '{fixture.Version}' is incompatible with Drill Press {fixture.Version}-incompatible. Install tool and SDK packages at {fixture.Version}-incompatible and rebuild the rule bundle. Releases require exact version matching.{Environment.NewLine}drillpress: Rule bundle exited 2.{Environment.NewLine}"
            ),
            result
        );
        Assert.Equal(
            """
            public static class Example
            {
                public static string Value => string.Empty;
            }
            """.ReplaceLineEndings("\n"),
            await FileSystem.File.ReadAllTextAsync(
                FileSystem.Path.Combine(fixture.Consumer, "Target", "Source.cs"),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Theory]
    [InlineData("DrillPress.Manifest")]
    [InlineData("DrillPress.RuleAuthoring")]
    [InlineData("DrillPress.Engine")]
    [InlineData("DrillPress.Testing")]
    public void Sdk_packages_include_documented_assemblies_and_exact_internal_dependencies(
        string id
    )
    {
        using var stream = FileSystem.File.OpenRead(
            FileSystem.Path.Combine(fixture.Feed, $"{id}.{fixture.Version}.nupkg")
        );
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);
        using var manifest = package.GetEntry(id + ".nuspec")!.Open();
        var metadata = XDocument.Load(manifest);

        var dependencies = metadata
            .Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .Where(element => element.Attribute("id")!.Value.StartsWith("DrillPress."));

        Assert.NotNull(package.GetEntry($"lib/net10.0/{id}.dll"));
        Assert.NotNull(package.GetEntry($"lib/net10.0/{id}.xml"));
        Assert.All(
            dependencies,
            dependency =>
                Assert.Equal($"[{fixture.Version}]", dependency.Attribute("version")!.Value)
        );
    }

    [Theory]
    [InlineData("DrillPress.Manifest")]
    [InlineData("DrillPress.RuleAuthoring")]
    [InlineData("DrillPress.Linq")]
    [InlineData("DrillPress.Engine")]
    [InlineData("DrillPress.Testing")]
    [InlineData("DrillPress.Cli")]
    public void Release_packages_include_readme_license_and_icon(string id)
    {
        using var stream = FileSystem.File.OpenRead(
            FileSystem.Path.Combine(fixture.Feed, $"{id}.{fixture.Version}.nupkg")
        );
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);
        using var manifest = package.GetEntry(id + ".nuspec")!.Open();

        var metadata = XDocument.Load(manifest);

        Assert.NotNull(package.GetEntry("README.md"));
        Assert.NotNull(package.GetEntry("LICENSE"));
        Assert.NotNull(package.GetEntry("icon.png"));
        Assert.Equal(
            "MIT",
            metadata.Descendants().Single(element => element.Name.LocalName == "license").Value
        );
        Assert.Equal(
            "icon.png",
            metadata.Descendants().Single(element => element.Name.LocalName == "icon").Value
        );
    }
}
