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

    public static RuleSet TargetEmptyRuleSet()
    {
        var rules = new RuleSet();
        var targetType = CodeType.Named("Sample.Target");
        rules.For(Code.MemberReferences.Where(new RuleCondition<MemberReference>(reference =>
                reference.ContainingType == targetType && reference.MemberName == "Empty")))
            .Forbid("TEST001", "Do not use Target.Empty.");
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
