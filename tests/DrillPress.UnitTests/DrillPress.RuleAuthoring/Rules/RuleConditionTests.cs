using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Rules;

public sealed class RuleConditionTests
{
    [Fact]
    public void Composition_and_query_exceptions_preserve_boolean_selection()
    {
        var startsWithE = new RuleCondition<MemberReference>(reference => reference.MemberName.StartsWith('E'));
        var included = new RuleCondition<MemberReference>(reference => reference.Location.FilePath == "Included.cs");
        var longName = new RuleCondition<MemberReference>(reference => reference.MemberName.Length > 3);
        var query = Code.MemberReferences.Where(startsWithE.And(included).Or(longName.Not()))
            .ExceptWhen(new(reference => reference.MemberName == "End"));
        var references = new[]
        {
            RuleTestData.Reference<string>("Empty", "Included.cs"),
            RuleTestData.Reference<string>("Empty", "Excluded.cs"),
            RuleTestData.Reference<string>("Any", "Excluded.cs"),
            RuleTestData.Reference<string>("End", "Included.cs"),
        };

        var diagnostics = RuleTestData.Evaluate(query, references);

        Assert.Equal([references[2].Location, references[0].Location], diagnostics.Select(diagnostic => diagnostic.Location));
    }

    [Fact]
    public void Require_reports_failed_conditions_at_the_selected_location()
    {
        var rules = new RuleSet();
        var location = new SourceLocation("Selected.cs", 4, 1, 1, 5);
        var condition = new RuleCondition<MemberReference>(reference => reference.MemberName == "Length")
            .ExceptWhen(new(reference => reference.Location.FilePath == "Excluded.cs"));
        rules.For(Code.MemberReferences).Require(condition, "TEST001", "Use Length.", _ => location);
        var references = new[] { RuleTestData.Reference<string>("Empty"), RuleTestData.Reference<string>("Length") };

        var diagnostics = rules.Evaluate(references);

        Assert.Equal([new RuleDiagnostic(new RuleDescriptor("TEST001", "Use Length."), location)], diagnostics);
    }

    [Fact]
    public void Boolean_composition_short_circuits_side_effect_free_conditions()
    {
        var never = new RuleCondition<MemberReference>(_ => false);
        var fail = new RuleCondition<MemberReference>(_ => throw new InvalidOperationException("Unexpected evaluation."));
        var rules = new RuleSet();
        rules.For(Code.MemberReferences.Where(never.And(fail).Or(never.Not().Or(fail))))
            .Forbid("TEST001", "Expected finding.");
        var reference = RuleTestData.Reference<string>("Empty");

        var diagnostics = rules.Evaluate([reference]);

        Assert.Equal([new RuleDiagnostic(new RuleDescriptor("TEST001", "Expected finding."), reference.Location)], diagnostics);
    }
    [Fact]
    public void Predicate_controls_candidates_when_used_by_a_query()
    {
        var condition = new RuleCondition<MemberReference>(reference => reference.Location.Start > 0);
        var query = Code.MemberReferences.Where(condition);

        var diagnostics = RuleTestData.Evaluate(
            query,
            RuleTestData.Reference<string>("First"),
            RuleTestData.Reference<string>("Second", start: 10));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(10, diagnostic.Location.Start);
    }
}
