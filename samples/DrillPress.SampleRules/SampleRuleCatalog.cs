using DrillPress;

namespace DrillPress.SampleRules;

public static class SampleRuleCatalog
{
    public static RuleCatalog Create(SourceBaseline? accepted = null)
    {
        var rules = new RuleCatalog();
        ShowcaseRules.Register(rules, accepted);
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
        rules
            .Rule("DP1006", "Inline locals that only rename their initializer.")
            .For(Code.LocalVariables.InNonTestProjects().WithSingleRead())
            .Forbid(fix: declaration => Fix.For(declaration).Inline().Propose());
        var layout = new FileLayout();
        rules
            .Rule(
                "DP1007",
                "Name the file after its only top-level type.",
                fixComplexity: RuleFixComplexity.Trivial
            )
            .For(Code.Files.Where(FileLayout.IsMisnamed))
            .Forbid(fix: FileLayout.Rename);
        rules
            .Rule(
                "DP1008",
                "Place the test file in the folder of the production file it tests.",
                fixComplexity: RuleFixComplexity.Trivial
            )
            .For(Code.Files.Where(file => layout.MirroredPath(file) is not null))
            .Forbid(fix: layout.Mirror);
        return rules;
    }
}
