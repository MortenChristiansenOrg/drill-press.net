namespace DrillPress.UnitTests.TestInfrastructure;

internal static class RuleTestData
{
    public static MemberReference Reference<TDeclaringType>(
        string memberName,
        string path = "Test.cs",
        int start = 0) =>
        new(
            CodeType.Of<TDeclaringType>(),
            memberName,
            new SourceLocation(path, start, memberName.Length, 1, start + 1));

    public static RuleSet StringEmptyRuleSet()
    {
        var rules = new RuleSet();
        rules.For(Code.MemberReferences.Where(Members.Are<string>(nameof(string.Empty))))
            .Forbid("DP1004", "Use the empty string literal instead of string.Empty.");
        return rules;
    }

    public static IReadOnlyList<RuleDiagnostic> Evaluate(
        CodeQuery<MemberReference> query,
        params MemberReference[] references)
    {
        var rules = new RuleSet();
        rules.For(query).Forbid("TEST001", "Test message.");
        return rules.Evaluate(references);
    }
}
