using DrillPress;

namespace DrillPress.SampleRules;

public static class SampleRuleSet
{
    public static RuleSet Create()
    {
        var rules = new RuleSet();
        ShowcaseRules.Register(rules);
        var tests = XunitTests.Methods;

        rules
            .For(tests)
            .Require(
                new(method => method.Body.EmptyLines.Count <= 2),
                "DP1001",
                "Keep at most two empty lines in a test.",
                method => method.Body.EmptyLines[2]
            );
        rules
            .For(tests.Where(new(method => method.Body.EarlyAssertion is not null)))
            .Forbid(
                "DP1002",
                "Move assertions after the final empty line.",
                method => method.Body.EarlyAssertion!
            );
        rules
            .For(
                Code.Interfaces.Where(
                    new(type => type.Solution.Implementations.HasExactlyOne(type))
                )
            )
            .Forbid(
                "DP1003",
                "Remove interfaces with exactly one concrete non-test implementation."
            );
        rules
            .For(CodeType.Of<string>().Member(nameof(string.Empty)).References)
            .Forbid(
                "DP1004",
                "Use the empty string literal \"\" instead of string.Empty.",
                fix: EmptyStringFix.Create
            );
        rules
            .For(
                CodeType
                    .Of<StringComparer>()
                    .Member(nameof(StringComparer.Ordinal))
                    .References.Where(OrdinalComparerFix.IsArgument)
            )
            .Forbid(
                "DP1005",
                "Avoid passing StringComparer.Ordinal.",
                fix: OrdinalComparerFix.Create
            );
        return rules;
    }
}
