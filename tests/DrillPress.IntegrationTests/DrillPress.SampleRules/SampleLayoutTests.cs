using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.SampleRules;

public sealed class SampleLayoutTests(SemanticRuleFixture fixture)
    : IntegrationTest,
        IClassFixture<SemanticRuleFixture>
{
    [Fact]
    public async Task Preserved_sample_layout_reports_violations_but_not_control_methods()
    {
        var library = fixture.Project(
            FileSystem.File.ReadAllText(
                RepositoryPath("Sample Solution", "src", "WidgetLibrary", "Contracts.cs")
            ),
            name: "WidgetLibrary"
        );
        var tests = fixture.Project(
            FileSystem.File.ReadAllText(
                RepositoryPath("Sample Solution", "tests", "WidgetLibrary.Tests", "WidgetTests.cs")
            ),
            name: "WidgetLibrary.Tests",
            isTest: true,
            dependencies: [library],
            extra: [new DocumentSnapshot("GlobalUsings.g.cs", "global using System;", true)]
        );

        var findings = await fixture.Describe(tests);

        Assert.Equal(
            [
                ("DP1001", 17, "", (string?)null),
                ("DP1002", 14, "Assert.Equal(string.Empty, store.Read())", null),
                ("DP1004", 14, "string.Empty", "\"\""),
            ],
            findings
        );
    }
}
