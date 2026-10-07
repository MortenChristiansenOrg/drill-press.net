using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Rules;

public sealed class RuleConditionTests
{
    [Fact]
    public void Composition_and_query_exceptions_preserve_boolean_selection()
    {
        var startsWithE = new RuleCondition<MemberReference>(reference =>
            reference.MemberName.StartsWith('E')
        );
        var included = new RuleCondition<MemberReference>(reference =>
            reference.Location.FilePath == "Included.cs"
        );
        var longName = new RuleCondition<MemberReference>(reference =>
            reference.MemberName.Length > 3
        );
        var query = Code
            .MemberReferences.Where(startsWithE.And(included).Or(longName.Not()))
            .ExceptWhen(
                new RuleCondition<MemberReference>(reference => reference.MemberName == "End")
            );
        var solution = RuleTestData.Solution(
            ("Included.cs", ["Empty", "End"]),
            ("Excluded.cs", ["Empty", "Any"])
        );

        var diagnostics = RuleTestData.Evaluate(query, solution);

        Assert.Equal(
            [
                ("TEST001", "Excluded.cs", "Sample.Target.Any"),
                ("TEST001", "Included.cs", "Sample.Target.Empty"),
            ],
            diagnostics.Select(RuleTestData.Describe)
        );
    }

    [Fact]
    public void Require_reports_failed_conditions_at_the_selected_location()
    {
        var rules = new RuleSet();
        var location = new SourceLocation("Target.cs", 0, 9, 1, 1);
        var condition = new RuleCondition<MemberReference>(reference =>
            reference.MemberName == "Length"
        ).ExceptWhen(
            new RuleCondition<MemberReference>(reference =>
                reference.Location.FilePath == "Excluded.cs"
            )
        );
        rules
            .Rule("TEST001", "Use Length.")
            .For(Code.MemberReferences)
            .ReportAt(_ => location)
            .Require(condition);
        var solution = RuleTestData.Solution(
            ("Selected.cs", ["Empty", "Length"]),
            ("Excluded.cs", ["Empty"])
        );

        var diagnostics = rules.Evaluate(solution);

        Assert.Equal([location, location], diagnostics.Select(diagnostic => diagnostic.Location));
    }

    [Fact]
    public void Boolean_composition_short_circuits_side_effect_free_conditions()
    {
        var never = new RuleCondition<MemberReference>(_ => false);
        var fail = new RuleCondition<MemberReference>(_ =>
            throw new InvalidOperationException("Unexpected evaluation.")
        );
        var rules = new RuleSet();
        rules
            .Rule("TEST001", "Expected finding.")
            .For(Code.MemberReferences.Where(never.And(fail).Or(never.Not().Or(fail))))
            .Forbid();
        var solution = RuleTestData.Solution(("A.cs", ["Empty"]));

        var diagnostics = rules.Evaluate(solution);

        Assert.Equal(
            [("TEST001", "A.cs", "Sample.Target.Empty")],
            diagnostics.Select(RuleTestData.Describe)
        );
    }

    [Fact]
    public void Predicate_controls_candidates_when_used_by_a_query()
    {
        var condition = new RuleCondition<MemberReference>(reference =>
            reference.Location.Line > 3
        );
        var query = Code.MemberReferences.Where(condition);
        var solution = RuleTestData.Solution(("A.cs", ["First", "Second", "Long"]));

        var diagnostics = RuleTestData.Evaluate(query, solution);

        Assert.Equal(
            [
                ("TEST001", "A.cs", "Sample.Target.Second"),
                ("TEST001", "A.cs", "Sample.Target.Long"),
            ],
            diagnostics.Select(RuleTestData.Describe)
        );
    }
}
