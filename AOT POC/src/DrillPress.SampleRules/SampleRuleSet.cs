using DrillPress;

namespace DrillPress.SampleRules;

public static class SampleRuleSet
{
    public static RuleSet Create()
    {
        var rules = new RuleSet();

        // Queries are ordinary, reusable C# values. More xUnit-specific rules can
        // build on this selection without repeating test-method identification.
        var xunitTests = Code.Methods.Where(Methods.HaveAnyAttribute(
            CodeType.Named("Xunit.FactAttribute"),
            CodeType.Named("Xunit.TheoryAttribute")));
        var onlyAssertionIsThrows = Methods.HaveOnlyAssertion(
            Assertions.Are(CodeType.Named("Xunit.Assert"), "Throws"));

        rules.For(xunitTests)
            .Require(
                id: "DP1001",
                condition: Methods.HaveAtMostEmptyLines(2),
                message: "Remove extra empty lines; keep at most two blank lines inside an xUnit test.",
                at: Methods.EmptyLineAfter(2))
            .Require(
                id: "DP1002",
                condition: Methods.HaveAllAssertionsAfterLastEmptyLine
                    .ExceptWhen(onlyAssertionIsThrows),
                message: "Move every assertion after the final blank line in the xUnit test.",
                at: Methods.FirstAssertionBeforeLastEmptyLine);

        rules.For(Code.Interfaces)
            .Require(
                id: "DP1003",
                condition: Interfaces.DoNotHaveExactlyOneNonTestImplementation,
                message: "Use the concrete type directly, or add another production implementation before introducing an interface.");

        rules.For(Code.MemberReferences.Where(Members.Are<string>(nameof(string.Empty))))
            .Forbid(
                id: "DP1004",
                message: "Use the empty string literal \"\" instead of string.Empty.",
                fix: Members.ReplaceWith("\"\""));

        rules.For(Code.MemberReferences.Where(Members.Are<StringComparer>(nameof(StringComparer.Ordinal))))
            .Forbid(
                id: "DP1005",
                message: "Use the API's default comparison by removing the StringComparer.Ordinal argument.",
                fix: Members.RemoveArgument);

        rules.For(Code.MemberReferences.Where(Members.Are<DateTime>(nameof(DateTime.Now))))
            .Forbid(
                id: "DP1006",
                message: "Inject TimeProvider and call GetLocalNow() so time-dependent code remains testable.");

        rules.For(Code.MemberReferences.Where(Members.Are<Thread>(nameof(Thread.Sleep))))
            .Forbid(
                id: "DP1007",
                message: "Use an asynchronous delay with cancellation instead of blocking a thread.");

        return rules;
    }
}
