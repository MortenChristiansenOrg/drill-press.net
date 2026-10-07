using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Rules;

public sealed class RuleClauseTests
{
    [Fact]
    public void Forbid_reports_each_candidate_and_returns_the_rule_for_further_clauses()
    {
        var rules = new RuleCatalog();
        var rule = rules.Rule("TEST001", "Test message.");
        var solution = RuleTestData.Solution(("A.cs", ["Empty"]));

        var returned = rule.For(Code.MemberReferences).Forbid();
        var diagnostics = rules.Evaluate(solution);

        Assert.Same(rule, returned);
        Assert.Equal(
            [("TEST001", "A.cs", "Sample.Target.Empty")],
            diagnostics.Select(RuleTestData.Describe)
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RuleFixComplexity.Trivial)]
    [InlineData(RuleFixComplexity.Local)]
    [InlineData(RuleFixComplexity.Complex)]
    [InlineData(RuleFixComplexity.Architectural)]
    public void Declarations_preserve_optional_complexity(RuleFixComplexity? complexity)
    {
        var rules = new RuleCatalog();
        rules.Rule("A", "Forbid.", fixComplexity: complexity).For(Code.MemberReferences).Forbid();
        rules
            .Rule("B", "Require.", fixComplexity: complexity)
            .For(Code.MemberReferences)
            .Require(_ => false);
        rules
            .Rule(new RuleDescriptor("C", "Descriptor.") { FixComplexity = complexity })
            .For(Code.MemberReferences)
            .Forbid();
        var solution = RuleTestData.Solution(("A.cs", ["Empty"]));

        var diagnostics = rules.Evaluate(solution);

        Assert.Equal(
            new[]
            {
                new RuleDescriptor("A", "Forbid.") { FixComplexity = complexity },
                new RuleDescriptor("B", "Require.") { FixComplexity = complexity },
                new RuleDescriptor("C", "Descriptor.") { FixComplexity = complexity },
            },
            diagnostics.Select(diagnostic => diagnostic.Descriptor)
        );
    }

    [Fact]
    public void Candidates_without_a_location_must_choose_one()
    {
        var rules = new RuleCatalog();
        rules
            .Rule("TEST001", "Pair.")
            .For(Code.MemberReferences.Select(reference => (reference, reference.MemberName)))
            .Forbid();
        var solution = RuleTestData.Solution(("A.cs", ["Empty"]));

        var exception = Assert.Throws<InvalidOperationException>(() => rules.Evaluate(solution));

        Assert.Equal(
            "Candidate type 'System.ValueTuple`2[DrillPress.MemberReference,System.String]' has no source location; choose one with ReportAt(...).",
            exception.Message
        );
    }

    [Fact]
    public void Report_at_anchors_candidates_without_their_own_location()
    {
        var rules = new RuleCatalog();
        rules
            .Rule("TEST001", "Pair.")
            .For(
                Code.MemberReferences.Select(reference =>
                    (Reference: reference, reference.MemberName)
                )
            )
            .ReportAt(pair => pair.Reference)
            .Require(pair => pair.MemberName != "Empty");
        var solution = RuleTestData.Solution(("A.cs", ["Any", "Empty"]));

        var diagnostics = rules.Evaluate(solution);

        Assert.Equal(
            [("TEST001", "A.cs", "Sample.Target.Empty")],
            diagnostics.Select(RuleTestData.Describe)
        );
    }

    [Fact]
    public void Report_at_null_keeps_the_candidate_location()
    {
        var rules = new RuleCatalog();
        rules
            .Rule("TEST001", "Reference.")
            .For(Code.MemberReferences)
            .ReportAt(reference =>
                reference.MemberName == "Any" ? (SourceLocation?)null : reference.Location
            )
            .Forbid();
        var solution = RuleTestData.Solution(("A.cs", ["Any", "Empty"]));

        var diagnostics = rules.Evaluate(solution);

        Assert.Equal(
            [("TEST001", "A.cs", "Sample.Target.Any"), ("TEST001", "A.cs", "Sample.Target.Empty")],
            diagnostics.Select(RuleTestData.Describe)
        );
    }

    [Fact]
    public void Report_at_rejects_locations_outside_the_candidate_compilation()
    {
        var rules = new RuleCatalog();
        rules
            .Rule("TEST001", "Reference.")
            .For(Code.MemberReferences)
            .ReportAt(_ => new SourceLocation("Missing.cs", 0, 1, 1, 1))
            .Forbid();
        var solution = RuleTestData.Solution(("A.cs", ["Any"]));

        var exception = Assert.Throws<ArgumentException>(() => rules.Evaluate(solution));

        Assert.Equal("location", exception.ParamName);
    }
}
