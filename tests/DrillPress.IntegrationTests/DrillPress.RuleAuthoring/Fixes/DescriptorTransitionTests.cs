using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class DescriptorTransitionTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Generic_static_and_reduced_calls_infer_retained_parameter_identity()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "using System; using System.Linq; class C { void M(string[] values) { _ = values.Distinct<string>(StringComparer.Ordinal); _ = Enumerable.Distinct<string>(values, StringComparer.Ordinal); } }"
                ),
            ]
        );
        var rules = new RuleSet();
        var distinct = CodeType.Framework("System.Linq.Enumerable").Member("Distinct");
        var enumerable = CodeType.Framework("System.Collections.Generic.IEnumerable<>");
        var comparer = CodeType.Framework("System.Collections.Generic.IEqualityComparer<>");
        var ordinal = CodeType.Of<StringComparer>().Member("Ordinal");
        rules
            .For(ordinal.References.PassedAs("comparer").To(distinct))
            .Forbid(
                "REMOVE",
                "Use default comparer.",
                fix: argument =>
                    Fix.For(argument)
                        .Remove()
                        .ExpectOverloadChange(
                            distinct.WithParameters(enumerable, comparer),
                            distinct.WithParameters(enumerable)
                        )
                        .RequireRemovedValue(change => change.RemovedValue!.RefersTo(ordinal))
                        .RequireRemovedEvaluation(change => change.RemovedValue!.RefersTo(ordinal))
                        .SafeWhen(change =>
                            change.Expected.Before.TypeArguments
                                is [{ SpecialType: SpecialType.System_String }]
                        )
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true, true], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(
            "using System; using System.Linq; class C { void M(string[] values) { _ = values.Distinct<string>(); _ = Enumerable.Distinct<string>(values); } }",
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData("static int Pick(int value) => value; static int Pick(string value) => 0;", "Pick")]
    [InlineData("static int Other(int renamed) => renamed;", "Other")]
    public async Task Ambiguous_targets_and_nonidentity_parameter_maps_withhold_removal(
        string overloads,
        string target
    )
    {
        var workspace = fixture.Workspace();
        var text =
            "class A { static int Pick(int value, bool flag) => value; "
            + overloads
            + " int M() => Pick(1, true); }";
        workspace.AddProject("Library", [new("A.cs", text)]);
        var owner = CodeType.Named("A");
        var from = owner.Member("Pick").WithParameters(CodeType.Of<int>(), CodeType.Of<bool>());
        var rules = new RuleSet();
        rules
            .For(Code.Calls.Calling(from).ArgumentsFor("flag"))
            .Forbid(
                "REMOVE",
                "Remove flag.",
                fix: argument =>
                    Fix.For(argument)
                        .Remove()
                        .ExpectOverloadChange(from, owner.Member(target))
                        .RequireRemovedValue(_ => true)
                        .RequireRemovedEvaluation(_ => true)
                        .SafeWhen(_ => true)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(text, result.FixedText("A.cs"));
    }

    [Fact]
    public void Framework_identity_rejects_source_defined_lookalikes()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "System.Linq",
            [new("A.cs", "namespace System.Linq { class Enumerable {} }")]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var type = Code.Types.In(solution).Single().Symbol;

        var trusted = CodeType.Framework("System.Linq.Enumerable").Matches(type);
        var named = CodeType.Named("System.Linq.Enumerable").Matches(type);

        Assert.False(trusted);
        Assert.True(named);
    }
}
