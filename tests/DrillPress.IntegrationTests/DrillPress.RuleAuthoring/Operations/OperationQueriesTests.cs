using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Operations;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class OperationQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Configured_overloads_preserve_array_rank_and_generic_element_identity()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { void M() => Save(new System.Collections.Generic.List<string>[1,1]); void Save(System.Collections.Generic.List<string>[,] values) { } }"
                ),
            ]
        );
        var expected = new CodeMember(
            CodeType.Named("A"),
            "Save",
            [CodeType.Of<List<string>[,]>()]
        );
        var wrongRank = new CodeMember(
            CodeType.Named("A"),
            "Save",
            [CodeType.Of<List<string>[]>()]
        );

        var call = OperationQueries
            .Invocations.In(workspace.Analyze(TestContext.Current.CancellationToken))
            .Single();

        Assert.True(call.Calls(expected));
        Assert.False(call.Calls(wrongRank));
    }

    [Fact]
    public void Calls_cover_initializers_accessors_expression_bodies_and_nested_functions_once()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { int x = F(1); int P => F(2); int Q { get { return F(3); } } void M() { int L() => F(4); L(); } static int F(int value) => value; }"
                ),
            ]
        );

        var calls = OperationQueries.Invocations.In(
            workspace.Analyze(TestContext.Current.CancellationToken)
        );

        Assert.Equal(
            ["F(1)", "F(2)", "F(3)", "F(4)", "L()"],
            calls.Select(call => call.Operation.Syntax.ToString())
        );
    }

    [Fact]
    public void Arguments_are_mapped_by_parameter_not_source_position()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { void M() => Save(count: 2, name: \"x\"); void Save(string name, int count, bool enabled = true) { } }"
                ),
            ]
        );

        var call = OperationQueries
            .Invocations.In(workspace.Analyze(TestContext.Current.CancellationToken))
            .Single();

        Assert.Equal("x", call.Argument("name")!.Value.ConstantValue.Value);
        Assert.Equal(2, call.Argument("count")!.Value.ConstantValue.Value);
        Assert.Equal(ArgumentKind.DefaultValue, call.Argument("enabled")!.ArgumentKind);
        Assert.True(
            call.Calls(
                new(
                    CodeType.Named("A"),
                    "Save",
                    [CodeType.Of<string>(), CodeType.Of<int>(), CodeType.Of<bool>()]
                )
            )
        );
    }
}
