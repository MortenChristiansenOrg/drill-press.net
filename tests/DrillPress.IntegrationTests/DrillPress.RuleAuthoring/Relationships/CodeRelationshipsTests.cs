using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Projects;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Relationships;

public sealed class CodeRelationshipsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Invocation_paths_follow_generated_implementations_without_reporting_them()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { void M() => Generated.Run(); }"),
            new("Generated.g.cs", "class Generated { public static void Run() => System.Threading.Thread.Sleep(1); }", true)]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var reaches = CodeRelationships.In(solution).Reaches(solution.Methods.Single().Symbol!, new(CodeType.Named("System.Threading.Thread"), "Sleep"));

        Assert.True(reaches);
        Assert.Equal(["M"], solution.Methods.Select(method => method.Name));
    }

    [Fact]
    public void Call_paths_cross_source_projects_terminate_cycles_and_exclude_uncalled_local_functions()
    {
        var workspace = fixture.Workspace();
        var dependency = workspace.AddProject("Dependency", [new("Dependency.cs", "public class Dependency { public static void Run() { System.Threading.Thread.Sleep(1); } }")]);
        workspace.AddProject("Consumer", [new("Consumer.cs", "class Consumer { void A() { B(); } void B() { A(); Dependency.Run(); } void C() { void Local() { Dependency.Run(); } } }")], dependencies: [dependency]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var graph = CodeRelationships.In(solution);
        var target = new CodeMember(CodeType.Named("System.Threading.Thread"), "Sleep");

        var paths = solution.Methods.Where(method => method.Source.Project.Snapshot.Name == "Consumer")
            .Select(method => (method.Symbol!.Name, graph.Reaches(method.Symbol, target))).ToArray();

        Assert.Equal([("A", true), ("B", true), ("C", false)], paths);
        Assert.Equal(["Dependency.Run()", "Dependency.Run()"], graph.CallersOf(solution.Methods.Single(method => method.Symbol!.Name == "Run").Symbol!)
            .Select(call => call.Operation.Syntax.ToString()));
    }

    [Fact]
    public void Project_relationships_keep_alternate_frameworks_separate()
    {
        var workspace = fixture.Workspace();
        var first = workspace.AddProject("Library", [new("Library.cs", "public interface I { }")], framework: "net9.0");
        var second = workspace.AddProject("Library", [new("Library.cs", "public interface I { }")], framework: "net10.0");
        var consumer = workspace.AddProject("Consumer", [new("Consumer.cs", "class C : I { }")], dependencies: [second]);
        var graph = new ProjectGraph(workspace.Analyze(TestContext.Current.CancellationToken));

        var dependencies = graph.DependenciesOf(consumer);
        var views = graph.CompatibleViewsOf(first);

        Assert.Equal([second, consumer], dependencies);
        Assert.Equal([first], Assert.Single(views));
        Assert.False(graph.Includes(consumer, first));
    }

    [Fact]
    public void Inheritance_and_file_ownership_preserve_partial_and_generated_semantics()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("I.cs", "public interface I { }"), new("A.cs", "partial class A : I { }"), new("B.cs", "partial class A { }"), new("C.g.cs", "class C : I { }", true)]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var graph = CodeRelationships.In(solution);
        var contract = solution.Types.Single(type => type.Symbol.Name == "I");

        var derived = graph.DerivedTypes(contract);
        var files = graph.FilesOf(derived.Single());

        Assert.Equal(["A"], derived.Select(type => type.Symbol.Name));
        Assert.Equal(["A.cs", "B.cs"], files.Select(source => source.Document.Path));
    }
}
