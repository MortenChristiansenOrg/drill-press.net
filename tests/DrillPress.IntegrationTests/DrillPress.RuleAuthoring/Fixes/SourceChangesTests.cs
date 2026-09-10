using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Queries;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class SourceChangesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Modifier_removal_preserves_comments_and_physical_lines()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", """
            // Kept documentation.
            internal // Kept explanation.
            class A { }
            """)]);
        var rules = new RuleSet();
        rules.For(Sources.Nodes<MemberDeclarationSyntax>().Where(node => node.Syntax is TypeDeclarationSyntax))
            .Forbid("ACCESS", "Use default accessibility.", fix: ModifierFix.RemoveRedundantAccessibility);

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal("""
            // Kept documentation.
            // Kept explanation.
            class A { }
            """, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Multi_file_proposals_validate_as_one_batch_and_produce_exact_after_text()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { int M() => 1; }"), new("B.cs", "class B { int M() => 1; }")]);
        var rules = new RuleSet();
        var query = CodeQuery<CodeFile>.Create(solution => Sources.Files.In(solution).Take(1));
        var sources = workspace.Analyze(TestContext.Current.CancellationToken).Projects.Single().Sources;
        var edits = sources.Select(source => SourceChanges.Replace(source,
            source.Tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().Single().Span, "2")).ToArray();
        rules.For(query).Forbid("EDIT", "Update both protocol constants.", fix: _ => SourceChanges.Propose(edits, _ => true));

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([new TestFinding("EDIT", "A.cs", 1, 1, "", true)], result.Findings);
        Assert.Equal("class A { int M() => 2; }", result.FixedText("A.cs"));
        Assert.Equal("class B { int M() => 2; }", result.FixedText("B.cs"));
    }

    [Fact]
    public async Task A_context_without_a_finding_can_withhold_the_whole_proposal()
    {
        var workspace = fixture.Workspace();
        var source = new TestSource("Shared.cs", "class Shared { int M() => 1; }");
        workspace.AddProject("First", [source]);
        workspace.AddProject("Second", [source]);
        var rules = new RuleSet();
        rules.For(Sources.Nodes<LiteralExpressionSyntax>().Where(node => node.Source.Project.Snapshot.Name == "First"))
            .Forbid("EDIT", "Update the constant.", fix: node => SourceChanges.Propose(
                [SourceChanges.Replace(node.Source, node.Syntax.Span, "2")], context => context.Original.Snapshot.Name != "Second"));

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([new TestFinding("EDIT", "Shared.cs", 1, 27, "1", false)], result.Findings);
        Assert.Equal(source.Text, result.FixedText("Shared.cs"));
    }

    [Fact]
    public async Task Compiler_errors_withhold_an_edit_even_when_the_consumer_accepts_it()
    {
        var workspace = fixture.Workspace();
        var source = new TestSource("A.cs", "class A { int M() => 1; }");
        workspace.AddProject("Library", [source]);
        var rules = new RuleSet();
        rules.For(Sources.Nodes<LiteralExpressionSyntax>()).Forbid("EDIT", "Update the constant.", fix: node =>
            SourceChanges.Propose([SourceChanges.Replace(node.Source, node.Syntax.Span, "\"wrong\"")], _ => true));

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([new TestFinding("EDIT", "A.cs", 1, 22, "1", false)], result.Findings);
        Assert.Equal(source.Text, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Modifier_removal_withholds_accessibility_changes()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "internal class A { internal void M() { } }")]);
        var rules = new RuleSet();
        rules.For(Sources.Nodes<MemberDeclarationSyntax>().Where(node => node.Syntax is TypeDeclarationSyntax or MethodDeclarationSyntax))
            .Forbid("ACCESS", "Remove redundant accessibility.", fix: ModifierFix.RemoveRedundantAccessibility);

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([
            new TestFinding("ACCESS", "A.cs", 1, 1, "internal class A { internal void M() { } }", true),
            new TestFinding("ACCESS", "A.cs", 1, 20, "internal void M() { }", false)], result.Findings);
        Assert.Equal("class A { internal void M() { } }", result.FixedText("A.cs"));
    }
}
