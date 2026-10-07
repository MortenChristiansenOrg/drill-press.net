using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class StringCheckPatternsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Built_in_patterns_match_by_kind_and_custom_patterns_keep_instance_identity()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Checks",
            [
                new(
                    "Checks.cs",
                    "class C { void M(string value) { if (value is null) {} if (value == \"\") {} if (string.IsNullOrEmpty(value)) {} if (string.IsNullOrWhiteSpace(value)) {} } }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var method = CodeType
            .Of<string>()
            .Member("IsNullOrEmpty")
            .WithParameters(CodeType.Of<string>());
        var custom = ConditionPattern.ForCall(method, "value", "empty");
        var other = ConditionPattern.ForCall(method, "value", "empty", matchingOutcome: false);
        var checks = Code.Methods.Named("M").Body().Conditions();

        var builtIn = checks
            .Checks(
                ConditionPattern.Null,
                ConditionPattern.EmptyString,
                ConditionPattern.NullOrEmptyString,
                ConditionPattern.NullOrWhiteSpaceString
            )
            .In(solution)
            .Select(match =>
                $"{match.Is(ConditionPattern.Null)}:{match.Is(ConditionPattern.EmptyString)}:{match.Is(ConditionPattern.NullOrEmptyString)}:{match.Is(ConditionPattern.NullOrWhiteSpaceString)}:{match.Is(custom)}"
            )
            .ToArray();
        var configured = checks
            .Checks(custom)
            .In(solution)
            .Select(match =>
                $"{match.Is(custom)}:{match.Is(other)}:{match.Is(ConditionPattern.EmptyString)}:{match.Is(ConditionPattern.NullOrEmptyString)}"
            )
            .ToArray();

        Assert.Equal(
            [
                "True:False:False:False:False",
                "False:True:False:False:False",
                "False:False:True:False:False",
                "False:False:False:True:False",
            ],
            builtIn
        );
        Assert.Equal(["True:False:False:False"], configured);
    }

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
        var empty = ConditionPattern.EmptyString;
        var patterns = new[]
        {
            empty,
            ConditionPattern.NullOrEmptyString,
            ConditionPattern.NullOrWhiteSpaceString,
            ConditionPattern.Null,
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
