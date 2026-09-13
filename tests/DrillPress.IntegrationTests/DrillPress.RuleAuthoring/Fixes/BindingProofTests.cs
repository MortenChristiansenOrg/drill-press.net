using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Queries;
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
}
