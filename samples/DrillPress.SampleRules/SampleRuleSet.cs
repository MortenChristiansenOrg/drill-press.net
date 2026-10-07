using DrillPress;

namespace DrillPress.SampleRules;

public static class SampleRuleSet
{
    public static RuleSet Create()
    {
        var rules = new RuleSet();
        ShowcaseRules.Register(rules);
        var tests = Code.TestMethods.Select(method => new TestBody(method));

        rules
            .Rule("DP1001", "Keep at most two empty lines in a test.")
            .For(tests)
            .ReportAt(test => test.EmptyLines[2])
            .Require(test => test.EmptyLines.Count <= 2);
        rules
            .Rule("DP1002", "Move assertions after the final empty line.")
            .For(tests.Where(test => test.EarlyAssertion is not null))
            .ReportAt(test => test.EarlyAssertion)
            .Forbid();
        rules
            .Rule("DP1003", "Remove interfaces with exactly one concrete non-test implementation.")
            .For(
                Code.Interfaces.ImplementationViews()
                    .IgnoringTestProjects()
                    .ConcreteOnly()
                    .WithExactlyOneImplementation()
            )
            .Forbid();
        rules
            .Rule(
                "DP1004",
                "Use the empty string literal \"\" instead of string.Empty.",
                fixComplexity: RuleFixComplexity.Trivial
            )
            .For(CodeType.Of<string>().Member(nameof(string.Empty)).References.OutsideNameOf())
            .Forbid(fix: EmptyStringFix.Create);
        rules
            .Rule("DP1005", "Avoid passing StringComparer.Ordinal.")
            .For(
                CodeType
                    .Of<StringComparer>()
                    .Member(nameof(StringComparer.Ordinal))
                    .References.Where(OrdinalComparerFix.IsArgument)
            )
            .Forbid(fix: OrdinalComparerFix.Create);
        return rules;
    }
}
