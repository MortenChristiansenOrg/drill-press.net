using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Analysis;

public sealed class CodeMethodTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Callees_follow_calls_within_the_type_or_beyond_directly_transitively_or_through_delegates()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Workers",
            [
                new(
                    "Worker.cs",
                    """
                    partial class Worker
                    {
                        void ExecuteAsync() { Loop(); Helper.Run(); System.Action later = () => Inner(); Recurse(); }
                        void Loop() { Step(); }
                        void Inner() { }
                        void Recurse() { Recurse(); }
                        void Unused() { }
                    }
                    static class Helper { public static void Run() => Other(); static void Other() { } }
                    """
                ),
                new("Worker.Step.cs", "partial class Worker { void Step() { } }"),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var execute = Code.Methods.Named("ExecuteAsync").In(solution).Single();

        var direct = execute.Callees();
        var ownType = execute.Callees(sameType: true);
        var ownTypeClosure = execute.Callees(sameType: true, transitive: true);
        var closure = execute.Callees(transitive: true);
        var withLambdas = execute.Callees(paths: CallPaths.Bound.ThroughDelegates());

        Assert.Equal(["Loop", "Run", "Recurse"], direct.Select(method => method.Name));
        Assert.Equal(["Loop", "Recurse"], ownType.Select(method => method.Name));
        Assert.Equal(["Loop", "Recurse", "Step"], ownTypeClosure.Select(method => method.Name));
        Assert.Equal(
            ["Loop", "Run", "Recurse", "Step", "Other"],
            closure.Select(method => method.Name)
        );
        Assert.Equal(
            ["Loop", "Run", "Inner", "Recurse"],
            withLambdas.Select(method => method.Name)
        );
        Assert.Equal("Worker.Step.cs", ownTypeClosure[2].Source.Document.Path);
        Assert.Same(Code.Methods.Named("Loop").In(solution).Single(), ownTypeClosure[0].Method);
    }
}
