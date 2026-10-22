using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class RewriteContextTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Mapping_uses_the_complete_batch_across_multiple_original_offsets()
    {
        var workspace = fixture.Workspace();
        var project = workspace.AddProject(
            "Library",
            [new("A.cs", "class A { int First() => 1; int Second() => 2; }")]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var literals = Code.Nodes<LiteralExpressionSyntax>().In(solution);
        var edits = literals
            .Select(node =>
                SourceChanges.Replace(node.Source, node.Syntax.Span, node.Syntax.ToString() + "0")
            )
            .ToArray();
        string[] mapped = [];
        var proposal = SourceChanges.Propose(
            edits,
            context =>
            {
                mapped = literals
                    .Select(node => context.Map(node.Source, node.Syntax)!.After.ToString())
                    .ToArray();
                return true;
            }
        );

        var safe = proposal.IsSafeIn(project);

        Assert.True(safe);
        Assert.Equal(["10", "20"], mapped);
    }

    [Fact]
    public void Replaced_descendants_are_not_guessed_from_matching_text()
    {
        var workspace = fixture.Workspace();
        var project = workspace.AddProject(
            "Library",
            [new("A.cs", "class A { int M() => 1 + 2; }")]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var binary = Code.Nodes<BinaryExpressionSyntax>().In(solution).Single();
        var edits = new[] { SourceChanges.Replace(binary.Source, binary.Syntax.Span, "2 + 1") };
        NodeRewrite? root = null;
        NodeRewrite? child = null;
        var proposal = SourceChanges.Propose(
            edits,
            context =>
            {
                root = context.Map(binary.Source, binary.Syntax);
                child = context.Map(binary.Source, binary.Syntax.Left);
                return true;
            }
        );

        var safe = proposal.IsSafeIn(project);

        Assert.True(safe);
        Assert.Equal("2 + 1", root!.After.ToString());
        Assert.Null(child);
    }

    [Fact]
    public async Task Source_symbols_correspond_across_the_original_and_rewritten_compilations()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    """
                    namespace Company.Time;
                    class Clock { public static int Now => 1; public static int Read() => 1; }
                    class Other { }
                    class A { int M() => Clock.Now; }
                    """
                ),
            ]
        );
        bool[] comparisons = [];
        var rules = new RuleCatalog();
        rules
            .Rule("CLOCK", "Read the clock.")
            .For(CodeType.Named("Company.Time.Clock").Member("Now").References)
            .Forbid(fix: reference =>
                Fix.For(reference)
                    .ReplaceWith("Clock.Read()")
                    .SafeWhen(change =>
                    {
                        var before = change.Before.Symbol!.ContainingType;
                        var after = change.After.Symbol!.ContainingType;
                        var other = change.Rewrite.Context.Rewritten.GetTypeByMetadataName(
                            "Company.Time.Other"
                        );
                        comparisons =
                        [
                            change.Same(before, after),
                            change.Same(before.ContainingNamespace, after.ContainingNamespace),
                            change.Same(before, other),
                            SymbolEqualityComparer.Default.Equals(before, after),
                        ];
                        return comparisons[0];
                    })
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal([true, true, false, false], comparisons);
    }
}
