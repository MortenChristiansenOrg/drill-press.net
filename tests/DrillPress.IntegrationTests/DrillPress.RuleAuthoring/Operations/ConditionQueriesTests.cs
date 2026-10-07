using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class ConditionQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData("string.IsNullOrWhiteSpace(input.Name)", "Error(\"Name\")", true)]
    [InlineData("((input.Name is null))", "Error(\"Name\")", true)]
    [InlineData("null == input.Name", "Error(nameof(input.Name))", true)]
    [InlineData("input.Name is null", "Error(\"Name\")", true)]
    [InlineData("string.IsNullOrEmpty(input.Name)", "Error(\"Name\")", true)]
    [InlineData("input.Name is null", "return", false)]
    [InlineData("input.Name is null", "Error(nameof(other.Name))", false)]
    [InlineData("input.Name is null", "Error(\"Other\")", false)]
    [InlineData("IsNullOrWhiteSpace(input.Name)", "Error(\"Name\")", false)]
    [InlineData("input.Name is null || input.Other is null", "Error(\"Name\")", false)]
    [InlineData("input.Optional is null", "Error(\"Optional\")", false)]
    public void Composed_policy_requires_bound_attribute_check_action_and_key(
        string condition,
        string action,
        bool expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "C.cs",
                    $$"""
                    class RequiredAttribute : System.Attribute { public bool AllowEmptyStrings { get; set; } }
                    class Model { [Required(AllowEmptyStrings = false)] public string? Name { get; set; } public string? Other { get; set; } public string? Optional { get; set; } }
                    class C {
                        void Error(string key) {}
                        bool IsNullOrWhiteSpace(string? value) => true;
                        void M(Model input, Model other) { _ = input; if ({{condition}}) {{action}}; }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var root = Code.Nodes<IdentifierNameSyntax>()
            .In(solution)
            .First(node => node.Syntax.Identifier.ValueText == "input");
        var resolver = new MemberKeyResolver();
        var patterns = new[]
        {
            ConditionPattern.Null,
            ConditionPattern.ForCall(
                CodeType.Of<string>().Member("IsNullOrEmpty"),
                "value",
                "empty"
            ),
            ConditionPattern.ForCall(
                CodeType.Of<string>().Member("IsNullOrWhiteSpace"),
                "value",
                "whitespace"
            ),
        };

        var matches = Code
            .Methods.Body()
            .Conditions()
            .Checks(patterns)
            .In(solution)
            .Where(check =>
                check.CheckedValue.Symbol is IPropertySymbol property
                && property
                    .Attributes()
                    .Where(attribute => attribute.Matches(CodeType.Named("RequiredAttribute")))
                    .Any(attribute =>
                        attribute.NamedArgument("AllowEmptyStrings")
                            is { HasValue: true, Value.Value: false }
                    )
            )
            .Where(check =>
                check
                    .MatchingBranch?.Calls()
                    .Any(call =>
                        call.Calls(CodeType.Named("C").Member("Error"))
                        && call.Parameter("key")?.Values.Single().Value is { } key
                        && resolver
                            .Resolve(key, new(root.Source, root.Syntax))
                            ?.CompareTo(check.CheckedValue) == PathCorrelation.Match
                    ) == true
            )
            .Select(check => check.Condition.Expression.Syntax.ToString())
            .ToArray();

        Assert.Equal(Enumerable.Repeat(condition, Convert.ToInt32(expected)), matches);
    }

    [Fact]
    public void Negation_selects_else_and_branch_traversal_excludes_deferred_functions()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "C.cs",
                    "class C { void Error() {} void M(string? value) { if (!string.IsNullOrEmpty(value)) {} else { void Later() { Error(); } Error(); } } }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var check = Assert.Single(
            Code.Methods.Body()
                .Conditions()
                .Checks(
                    ConditionPattern.ForCall(
                        CodeType.Of<string>().Member("IsNullOrEmpty"),
                        "value",
                        "empty"
                    )
                )
                .In(solution)
        );

        Assert.False(check.MatchingOutcome);
        Assert.Equal(
            ["Error()"],
            check.MatchingBranch!.Calls().Select(call => call.Operation.Syntax.ToString())
        );
    }
}
