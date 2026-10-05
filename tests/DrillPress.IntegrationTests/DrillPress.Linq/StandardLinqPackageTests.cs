using System.Xml.Linq;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.Linq;

[Collection(typeof(PackageCollection))]
public sealed class StandardLinqPackageTests(PackageFixture fixture) : IntegrationTest
{
    [Fact]
    public async Task Optional_catalogue_builds_and_classifies_calls_from_packages_only()
    {
        var directory = FileSystem.Directory.CreateDirectory(
            FileSystem.Path.Combine(fixture.Consumer, "LinqCatalogue")
        );
        var project = new XElement(
            "Project",
            new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement(
                "PropertyGroup",
                new XElement("TargetFramework", "net10.0"),
                new XElement("OutputType", "Exe"),
                new XElement("ImplicitUsings", "enable")
            ),
            new XElement(
                "ItemGroup",
                new XElement(
                    "PackageReference",
                    new XAttribute("Include", "DrillPress.Linq"),
                    new XAttribute("Version", $"[{fixture.Version}]")
                ),
                new XElement(
                    "PackageReference",
                    new XAttribute("Include", "DrillPress.Testing"),
                    new XAttribute("Version", $"[{fixture.Version}]")
                )
            )
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory.FullName, "Consumer.csproj"),
            project.ToString(),
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory.FullName, "Program.cs"),
            """
            using DrillPress;
            using DrillPress.Testing;
            using DrillPress.Presets;
            var workspace = new RuleTestWorkspace();
            workspace.AddProject("Consumer", [new("Calls.cs",
                "using System.Linq; class C { void M(int[] items) { _ = items.Where(x => x > 0); _ = items.Count(); } }")]);
            var rules = new RuleSet();
            rules.For(Code.Calls.Where(call => StandardLinq.Inspect(call).Category == LinqOperationCategory.Scalar))
                .Forbid("SCALAR", "Found scalar.");
            var result = await workspace.CheckAsync(rules);
            Console.Write(result.Findings.Single().Text);
            """,
            TestContext.Current.CancellationToken
        );
        await fixture.RequireDotnetAsync(
            directory.FullName,
            "build",
            "-c",
            "Release",
            "--configfile",
            fixture.Config
        );

        var result = await fixture.DotnetAsync(
            directory.FullName,
            "run",
            "-c",
            "Release",
            "--no-build",
            "--no-restore"
        );

        Assert.Equal((0, "items.Count()", ""), result);
    }
}
