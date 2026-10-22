using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ArgumentAdditionTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task An_expected_overload_change_adds_a_positional_argument()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "using System; class A { bool M(string text) => text.StartsWith(\"x\"); }"
                ),
            ]
        );
        var startsWith = CodeType.Of<string>().Member(nameof(string.StartsWith));
        var ordinal = CodeType.Of<StringComparison>().Member(nameof(StringComparison.Ordinal));
        var rules = new RuleCatalog();
        rules
            .Rule("ORDINAL", "Compare ordinally.")
            .For(Code.Calls.To(startsWith.WithParameters(CodeType.Of<string>())))
            .Forbid(fix: call =>
                Fix.For(call)
                    .AddArgument("comparisonType", "StringComparison.Ordinal")
                    .ExpectOverloadChange(
                        from: startsWith.WithParameters(CodeType.Of<string>()),
                        to: startsWith.WithParameters(
                            CodeType.Of<string>(),
                            CodeType.Of<StringComparison>()
                        )
                    )
                    .SafeWhen(change => change.AddedValue.RefersTo(ordinal))
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            "using System; class A { bool M(string text) => text.StartsWith(\"x\", StringComparison.Ordinal); }",
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData(
        "void Run(int value, CancellationToken token = default) {} void M() => Run(1);",
        "void Run(int value, CancellationToken token = default) {} void M() => Run(1, CancellationToken.None);"
    )]
    [InlineData(
        "void Run(int value = 0, int other = 0, CancellationToken token = default) {} void M() => Run(1);",
        "void Run(int value = 0, int other = 0, CancellationToken token = default) {} void M() => Run(1, token: CancellationToken.None);"
    )]
    [InlineData(
        "void Run(CancellationToken token = default) {} void M() => Run();",
        "void Run(CancellationToken token = default) {} void M() => Run(CancellationToken.None);"
    )]
    public async Task An_omitted_optional_argument_is_supplied_in_the_same_overload(
        string members,
        string expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "using System.Threading; class A { " + members + " }")]
        );

        var result = await workspace.CheckAsync(Tokens(), TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            "using System.Threading; class A { " + expected + " }",
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task A_missing_parameter_without_an_expected_overload_change_is_explained()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "class A { void Run(int value) {} void M() => Run(1); }")]
        );

        var result = await workspace.CheckAsync(Tokens(), TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            TOKEN Pass the token.
            A.cs
              1:46
                fix withheld: Run has no token parameter; declare the overload change with ExpectOverloadChange

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
    }

    private static RuleCatalog Tokens()
    {
        var none = CodeType.Of<CancellationToken>().Member(nameof(CancellationToken.None));
        var rules = new RuleCatalog();
        rules
            .Rule("TOKEN", "Pass the token.")
            .For(Code.Calls.ToMethodsNamed("Run"))
            .Forbid(fix: call =>
                Fix.For(call)
                    .AddArgument("token", "CancellationToken.None")
                    .SafeWhen(change => change.AddedValue.RefersTo(none))
            );
        return rules;
    }
}
