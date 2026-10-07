using DrillPress.Testing;
using Xunit;

namespace MyRules.Tests;

public sealed class TemplateExtractionRulesTests
{
    [Fact]
    public async Task A_string_parameter_template_has_a_proven_fix()
    {
        const string source = """
            class Routes
            {
                string First(string value) => $"/api/{value}";
                string Second(string value) => $"/api/{value}";
            }
            """;

        var result = await CheckAsync(source);

        Assert.Equal(
            """
            TEMPLATE Extract the repeated string template.
            App.cs
              +3:35

            """.ReplaceLineEndings("\n"),
            result.Output
        );
        Assert.Equal(
            """
            class Routes
            {
                string First(string value) => global::Routes.FormatValue(value);
                string Second(string value) => global::Routes.FormatValue(value);
                private static string FormatValue(string value) => $"/api/{value}";
            }
            """.ReplaceLineEndings("\n"),
            result.FixedText("App.cs")
        );
    }

    [Theory]
    [InlineData(",8")]
    [InlineData(":custom")]
    public async Task Formatting_outside_the_policy_keeps_the_finding_without_a_fix(
        string formatting
    )
    {
        var source = $$"""
            class Routes
            {
                string First(string value) => $"/api/{value{{formatting}}}";
                string Second(string value) => $"/api/{value{{formatting}}}";
            }
            """;

        var result = await CheckAsync(source);

        Assert.Equal(
            """
            TEMPLATE Extract the repeated string template.
            App.cs
              3:35

            """.ReplaceLineEndings("\n"),
            result.Output
        );
        Assert.Equal(source, result.FixedText("App.cs"));
    }

    private static async Task<RuleTestResult> CheckAsync(string source)
    {
        var workspace = new RuleTestWorkspace();
        workspace.AddProject("App", [new TestSource("App.cs", source)]);
        return await workspace.CheckAsync(
            TemplateExtractionRules.Create(),
            TestContext.Current.CancellationToken
        );
    }
}
