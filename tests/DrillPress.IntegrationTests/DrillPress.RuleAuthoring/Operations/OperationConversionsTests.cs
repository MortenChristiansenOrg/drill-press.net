using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class OperationConversionsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Implicit_conversions_are_removed_and_casts_only_when_requested()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    "class A { void M(int value) { object box = value; object cast = (object)value; } }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var conversions = Code.Operations<IConversionOperation>().In(solution);
        var implicitBox = conversions.Single(node => node.Operation.IsImplicit).Operation;
        var cast = conversions.Single(node => !node.Operation.IsImplicit).Operation;

        var values = new[]
        {
            implicitBox.WithoutConversions(),
            cast.WithoutConversions(),
            cast.WithoutConversions(includeExplicit: true),
        }
            .Select(operation => operation.Syntax.ToString())
            .ToArray();

        Assert.Equal(["value", "(object)value", "value"], values);
    }
}
