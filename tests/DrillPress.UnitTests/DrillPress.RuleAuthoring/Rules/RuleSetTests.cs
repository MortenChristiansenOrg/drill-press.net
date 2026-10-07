using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Rules;

public sealed class RuleSetTests
{
    [Fact]
    public void Evaluate_orders_diagnostics_by_rule_path_and_source_position()
    {
        var rules = new RuleSet();
        rules.Rule("Z002", "Later rule.").For(Code.MemberReferences).Forbid();
        rules.Rule("A001", "Earlier rule.").For(Code.MemberReferences).Forbid();
        var solution = RuleTestData.Solution(("B.cs", ["Second"]), ("A.cs", ["First", "Any"]));

        var diagnostics = rules.Evaluate(solution);

        Assert.Equal(
            [
                ("A001", "A.cs", "Sample.Target.First"),
                ("A001", "A.cs", "Sample.Target.Any"),
                ("A001", "B.cs", "Sample.Target.Second"),
                ("Z002", "A.cs", "Sample.Target.First"),
                ("Z002", "A.cs", "Sample.Target.Any"),
                ("Z002", "B.cs", "Sample.Target.Second"),
            ],
            diagnostics.Select(RuleTestData.Describe)
        );
    }

    [Fact]
    public void Rule_rejects_duplicate_rule_ids()
    {
        var rules = new RuleSet();
        rules.Rule("TEST001", "First message.").For(Code.MemberReferences).Forbid();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            rules.Rule("TEST001", "Second message.")
        );

        Assert.Equal("Rule id 'TEST001' is registered more than once.", exception.Message);
    }

    [Theory]
    [InlineData("", "Message")]
    [InlineData("TEST001", "")]
    [InlineData("TEST\n001", "Message")]
    [InlineData("TEST001", "Message\rnext")]
    [InlineData("TEST001", "Message\u2028next")]
    public void Rule_rejects_blank_or_multiline_rule_identity_or_message(string id, string message)
    {
        var rules = new RuleSet();

        Assert.Throws<ArgumentException>(() => rules.Rule(id, message));
    }

    [Fact]
    public void A_rule_without_a_terminated_clause_fails_evaluation_with_guidance()
    {
        var rules = new RuleSet();
        rules.Rule("TEST001", "Forgotten.").For(Code.MemberReferences);
        var solution = RuleTestData.Solution();

        var exception = Assert.Throws<InvalidOperationException>(() => rules.Evaluate(solution));

        Assert.Equal(
            "Rule 'TEST001' has no clause; end each For(...) with Forbid(...) or Require(...).",
            exception.Message
        );
    }

    [Fact]
    public void Cancelled_analysis_stops_even_when_no_candidates_exist()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var solution = new AnalysisSolution([], cancellation.Token);
        var rules = new RuleSet();
        rules.Rule("TEST001", "Stop evaluation.").For(Code.MemberReferences).Forbid();

        Assert.Throws<OperationCanceledException>(() => rules.Evaluate(solution));
    }

    [Fact]
    public void Invalid_complexity_is_rejected_at_registration()
    {
        var rules = new RuleSet();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            rules.Rule("R", "Message.", fixComplexity: (RuleFixComplexity)99)
        );

        Assert.Equal("descriptor", exception.ParamName);
    }
}
