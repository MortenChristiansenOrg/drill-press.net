using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class BindingProofTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Rebinding_rejects_a_new_enclosing_overload_even_when_both_programs_compile()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { void M() => Pick(Value()); int Value() => 1; void Pick(byte value) { } void Pick(long value) { } }"
                ),
            ]
        );
        var node = Sources
            .Nodes<InvocationExpressionSyntax>()
            .In(workspace.Analyze(TestContext.Current.CancellationToken))
            .Single(candidate => candidate.Syntax.ToString() == "Value()");

        var preserves = BindingProof.PreservesEnclosingExpressions(node.Source, node.Syntax, "1");

        Assert.False(preserves);
    }

    [Fact]
    public void Argument_removal_does_not_grant_an_exception_for_a_specific_library_overload()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "using System; using System.Linq; class A { object M(string[] values) => values.Distinct(StringComparer.Ordinal); }"
                ),
            ]
        );
        var node = Sources
            .Nodes<ArgumentListSyntax>()
            .In(workspace.Analyze(TestContext.Current.CancellationToken))
            .Single();

        var preserves = BindingProof.PreservesEnclosingExpressions(node.Source, node.Syntax, "()");

        Assert.False(preserves);
    }
}
