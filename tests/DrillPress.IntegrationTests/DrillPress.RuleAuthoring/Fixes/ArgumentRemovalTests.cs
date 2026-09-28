using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Operations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ArgumentRemovalTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData(ProofResult.Unknown, false)]
    [InlineData(ProofResult.Proven, true)]
    public async Task Changed_synthesized_defaults_need_their_own_scoped_approval(
        ProofResult approval,
        bool expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { static string Pick(string value, int option) => value; static string Pick(string value, bool mode = false) => value; string M() => Pick(\"x\", 0); }"
                ),
            ]
        );
        var transition = new MethodTransition(
            project =>
            {
                var methods = project
                    .Compilation.GetTypeByMetadataName("A")!
                    .GetMembers("Pick")
                    .OfType<IMethodSymbol>()
                    .ToArray();
                return new(
                    methods.Single(method =>
                        method.Parameters[1].Type.SpecialType == SpecialType.System_Int32
                    ),
                    methods.Single(method =>
                        method.Parameters[1].Type.SpecialType == SpecialType.System_Boolean
                    )
                );
            },
            new Dictionary<string, string> { ["value"] = "value" }
        );
        var rules = new RuleSet();
        rules
            .For(OperationQueries.Invocations.Calling(CodeType.Named("A").Member("Pick")))
            .Forbid(
                "ARGUMENT",
                "Omit the approved default.",
                fix: call =>
                    Fix.For(call)
                        .RemoveArgument("option")
                        .RequireTransition(transition)
                        .RequireRemovedValue(_ => ProofResult.Proven)
                        .RequireRemovedEvaluation(_ => ProofResult.Proven)
                        .RequireSynthesizedArguments(change =>
                            change.After.Arguments.Any(argument =>
                                argument.ArgumentKind == ArgumentKind.DefaultValue
                                && argument.Value.ConstantValue.Value is false
                            )
                                ? approval
                                : ProofResult.Unknown
                        )
                        .Propose(_ => ProofResult.Proven)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([expected], result.Findings.Select(finding => finding.HasFix));
    }

    [Theory]
    [InlineData("NextA().Choose(Options.Default, NextB())", "NextA().Choose(NextB())")]
    [InlineData(
        "NextA().Choose(second: NextB(), option: Options.Default)",
        "NextA().Choose(second: NextB())"
    )]
    [InlineData(
        "Api.Choose(second: NextB(), option: Options.Default, first: NextA())",
        "Api.Choose(second: NextB(), first: NextA())"
    )]
    public async Task Exact_transitions_preserve_named_argument_and_receiver_evaluation_order(
        string call,
        string replacement
    )
    {
        var workspace = fixture.Workspace();
        var source = Source(call);
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(
            Rules(ProofResult.Proven),
            TestContext.Current.CancellationToken
        );

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(source.Replace(call, replacement), result.FixedText("A.cs"));
    }

    [Theory]
    [InlineData("NextA().Choose(Options.Default, NextB())", ProofResult.Unknown)]
    [InlineData("NextA().Choose(Options.Other, NextB())", ProofResult.Proven)]
    [InlineData("NextA().Choose(option: 0, second: NextB())", ProofResult.Proven)]
    [InlineData("NextA().Choose(/* keep */ Options.Default, NextB())", ProofResult.Proven)]
    public async Task Unapproved_getter_evaluation_value_or_trivia_keeps_the_finding(
        string call,
        ProofResult evaluation
    )
    {
        var workspace = fixture.Workspace();
        var source = Source(call);
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(
            Rules(evaluation),
            TestContext.Current.CancellationToken
        );

        Assert.False(Assert.Single(result.Findings).HasFix);
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task An_unexpected_constructed_generic_destination_is_not_approved_by_behavior_proof()
    {
        var workspace = fixture.Workspace();
        var source = Source("NextA().Choose(Options.Default, NextB())");
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(
            Rules(ProofResult.Proven, SpecialType.System_Object),
            TestContext.Current.CancellationToken
        );

        Assert.False(Assert.Single(result.Findings).HasFix);
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Same_spelling_in_another_assembly_does_not_match_the_configured_pair()
    {
        var workspace = fixture.Workspace();
        var source = Source("NextA().Choose(Options.Default, NextB())");
        workspace.AddProject("Lookalike", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(
            Rules(ProofResult.Proven),
            TestContext.Current.CancellationToken
        );

        Assert.False(Assert.Single(result.Findings).HasFix);
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    private static RuleSet Rules(
        ProofResult evaluation,
        SpecialType destination = SpecialType.System_String
    )
    {
        var transition = new MethodTransition(
            project =>
            {
                var type = project.Compilation.GetTypeByMetadataName("Api")!;
                var methods = type.GetMembers("Choose").OfType<IMethodSymbol>().ToArray();
                return type.ContainingAssembly.Name == "Library"
                    ? new(
                        methods
                            .Single(method => method.Parameters.Length == 3)
                            .Construct(
                                project.Compilation.GetSpecialType(SpecialType.System_String)
                            ),
                        methods
                            .Single(method => method.Parameters.Length == 2)
                            .Construct(project.Compilation.GetSpecialType(destination))
                    )
                    : null;
            },
            new Dictionary<string, string> { ["first"] = "first", ["second"] = "second" }
        );
        var rules = new RuleSet();
        rules
            .For(OperationQueries.Invocations.Calling(CodeType.Named("Api").Member("Choose")))
            .Forbid(
                "ARGUMENT",
                "Omit the approved default.",
                fix: call =>
                    Fix.For(call)
                        .RemoveArgument("option")
                        .RequireTransition(transition)
                        .RequireRemovedValue(change =>
                            change.Removed.Operation
                                is IPropertyReferenceOperation
                                {
                                    Property.Name: "Default",
                                    Property.ContainingType.Name: "Options"
                                }
                                ? ProofResult.Proven
                                : ProofResult.Unknown
                        )
                        .RequireRemovedEvaluation(_ => evaluation)
                        .Propose(_ => ProofResult.Proven)
            );
        return rules;
    }

    private static string Source(string call) =>
        $$"""
            static class Options { public static int Default => 0; public static int Other => 1; }
            static class Api {
                public static T Choose<T>(this T first, int option, T second) => first;
                public static T Choose<T>(this T first, T second) => first;
            }
            class A { string NextA() => "a"; string NextB() => "b"; string M() => {{call}}; }
            """;
}
