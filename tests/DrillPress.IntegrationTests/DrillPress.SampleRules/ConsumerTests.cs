using System.Xml.Linq;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.SampleRules;

public sealed class ConsumerTests : IntegrationTest
{
    [Fact]
    public void Sample_rule_bundle_references_only_DrillPress_projects()
    {
        var path = RepositoryPath("samples", "DrillPress.SampleRules", "DrillPress.SampleRules.csproj");

        var project = XDocument.Parse(FileSystem.File.ReadAllText(path));

        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Equal(
            ["../../src/DrillPress.RuleAuthoring/DrillPress.RuleAuthoring.csproj",
             "../../src/DrillPress.Engine/DrillPress.Engine.csproj"],
            project.Descendants("ProjectReference").Select(reference => reference.Attribute("Include")!.Value));
    }
}
