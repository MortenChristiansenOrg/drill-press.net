using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Queries;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.Testing;

public sealed class RuleTestWorkspaceTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Generated_custom_candidates_never_report_and_linked_frameworks_withhold_inactive_fixes()
    {
        var workspace = fixture.Workspace();
        var source = new TestSource(
            "Shared.cs",
            """
            class Shared
            {
            #if FEATURE
                string M() => string.Empty;
            #endif
            }
            """
        );
        workspace.AddProject(
            "Library",
            [source, new("Generated.g.cs", "class Generated { }", true)],
            framework: "net9.0",
            symbols: ["FEATURE"]
        );
        workspace.AddProject("Library", [source], framework: "net10.0");
        var rules = new RuleSet();
        var allFiles = CodeQuery<CodeFile>.Create(solution =>
            solution
                .Projects.SelectMany(project => project.Sources)
                .Select(source => new CodeFile(source))
        );
        rules
            .For(allFiles.Where(file => file.Source.Document.IsGenerated))
            .Forbid("GENERATED", "Do not report generated files.");
        rules
            .For(Code.MemberReferences.Where(Members.Are<string>(nameof(string.Empty))))
            .Forbid("EMPTY", "Use a literal.", fix: EmptyStringFix.Create);

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            [new TestFinding("EMPTY", "Shared.cs", 4, 19, "string.Empty", false)],
            result.Findings
        );
        Assert.Equal(source.Text, result.FixedText("Shared.cs"));
    }

    [Fact]
    public async Task Conflicting_fixes_are_withheld_as_complete_batches()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { int M() => 1; }")]);
        var rules = new RuleSet();
        var literals = Sources.Nodes<LiteralExpressionSyntax>();
        rules
            .For(literals)
            .Forbid(
                "FIRST",
                "Use two.",
                fix: node =>
                    SourceChanges.Propose(
                        [SourceChanges.Replace(node.Source, node.Syntax.Span, "2")],
                        _ => true
                    )
            );
        rules
            .For(literals)
            .Forbid(
                "SECOND",
                "Use three.",
                fix: node =>
                    SourceChanges.Propose(
                        [SourceChanges.Replace(node.Source, node.Syntax.Span, "3")],
                        _ => true
                    )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new TestFinding("FIRST", "A.cs", 1, 22, "1", false),
                new TestFinding("SECOND", "A.cs", 1, 22, "1", false),
            ],
            result.Findings
        );
        Assert.Equal("class A { int M() => 1; }", result.FixedText("A.cs"));
    }
}
