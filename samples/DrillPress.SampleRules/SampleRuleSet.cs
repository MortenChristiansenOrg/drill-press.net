using DrillPress;

namespace DrillPress.SampleRules;

public static class SampleRuleSet
{
    public static RuleSet Create()
    {
        var rules = new RuleSet();
        rules.For(Code.MemberReferences.Where(Members.Are<string>(nameof(string.Empty))))
            .Forbid(
                id: "DP1004",
                message: "Use the empty string literal \"\" instead of string.Empty.");
        return rules;
    }
}
