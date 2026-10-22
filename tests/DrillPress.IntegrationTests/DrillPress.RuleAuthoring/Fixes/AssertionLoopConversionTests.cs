using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class AssertionLoopConversionTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData("var", "Assert.All(items, item => Assert.True(item > 0));", true)]
    [InlineData("int", "foreach (int item in items) Assert.True(item > 0);", false)]
    public async Task Assertion_loops_require_var_and_preserve_the_inner_assertion(
        string type,
        string expected,
        bool hasFix
    )
    {
        var workspace = fixture.Workspace();
        var source = """
            using Xunit;
            class A { void Check(int[] items) { BODY } }
            namespace Xunit { static class Assert {
                public static void True(bool value) {}
                public static void All<T>(System.Collections.Generic.IEnumerable<T> items, System.Action<T> assertion) {}
            } }
            """;
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    source.Replace("BODY", $"foreach ({type} item in items) Assert.True(item > 0);")
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("ALL", "Assert every item.")
            .For(Code.Nodes<ForEachStatementSyntax>())
            .Forbid(fix: loop => Fix.For(loop).UseAssertAll().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([hasFix], result.Findings.Select(finding => finding.HasFix).ToArray());
        Assert.Equal(source.Replace("BODY", expected), result.FixedText("A.cs"));
    }
}
