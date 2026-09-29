using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Queries;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class StringCheckPatternsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Empty_checks_retain_property_identity_polarity_and_separate_null_semantics()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Checks",
            [
                new(
                    "Checks.cs",
                    """
                    class C {
                        string Value => "";
                        static void Bad() {}
                        void M() {
                            if (Value == string.Empty) Bad(); if (string.Empty == Value) Bad();
                    if (Value != string.Empty) {} else Bad();
                    if (Value == "") Bad(); if ("" == Value) Bad();
                            if (Value != "") {} else Bad(); if (Value is "") Bad();
                            if (Value is not "") {} else Bad(); if (Value.Length == 0) Bad();
                            if (0 != Value.Length) {} else Bad();
                            if (string.IsNullOrEmpty(Value)) Bad();
                            if (!string.IsNullOrWhiteSpace(Value)) {} else Bad();
                            if (Value == null) Bad();
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var empty = ConditionPattern.IsEmptyString();
        var patterns = new[]
        {
            empty,
            ConditionPattern.IsNullOrEmptyString(),
            ConditionPattern.IsNullOrWhiteSpaceString(),
            ConditionPattern.IsNull(),
        };

        var results = Code
            .Methods.Named("M")
            .Body()
            .Conditions()
            .Checks(patterns)
            .In(solution)
            .Select(match =>
                $"{match.Pattern.Kind}:{match.CheckedProperty?.Name}:{match.MatchingOutcome}:{match.MatchingBranch?.Calls(CodeType.Named("C").Member("Bad"))}:{match.Is(empty)}"
            )
            .ToArray();

        Assert.Equal(
            [
                "EmptyString:Value:True:True:True",
                "EmptyString:Value:True:True:True",
                "EmptyString:Value:False:True:True",
                "EmptyString:Value:True:True:True",
                "EmptyString:Value:True:True:True",
                "EmptyString:Value:False:True:True",
                "EmptyString:Value:True:True:True",
                "EmptyString:Value:False:True:True",
                "EmptyString:Value:True:True:True",
                "EmptyString:Value:False:True:True",
                "NullOrEmptyString:Value:True:True:False",
                "NullOrWhiteSpaceString:Value:False:True:False",
                "Null:Value:True:True:False",
            ],
            results
        );
    }
}
