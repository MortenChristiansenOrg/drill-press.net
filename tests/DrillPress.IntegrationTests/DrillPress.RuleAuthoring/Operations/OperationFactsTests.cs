using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class OperationFactsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Receiver_and_initializer_evidence_do_not_infer_later_assignments()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    "class A { void M(int value) { var text = \"first\"; text = \"second\"; text.Contains(\"a\"); object box = (object)value; } }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var call = Code.Calls.ToMethodsNamed("Contains").In(solution).Single();
        var receiver = call.Operation.ReceiverValue()!;
        var conversion = Code.Operations<IConversionOperation>()
            .In(solution)
            .Single(node => !node.Operation.IsImplicit)
            .Operation;

        Assert.Equal("text", receiver.ReferencedVariable()?.Name);
        Assert.Equal(
            "\"first\"",
            receiver.ReferencedVariable()!.Initializer(call.Source)?.Syntax.ToString()
        );
        Assert.Equal("MethodBodyOperation", call.Operation.RootOperation().GetType().Name);
        Assert.Same(conversion, conversion.WithoutConversions());
        Assert.Equal(
            "value",
            conversion.WithoutConversions(includeExplicit: true)?.ReferencedVariable()?.Name
        );
    }
}
