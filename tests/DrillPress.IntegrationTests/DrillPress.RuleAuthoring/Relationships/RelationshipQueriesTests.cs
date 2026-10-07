using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Relationships;

public sealed class RelationshipQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Body_errors_do_not_hide_resolved_override_edges_but_signature_errors_do()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Product",
            [
                new(
                    "P.cs",
                    """
                    class Base { public virtual void Run(int value) {} }
                    class BrokenBody : Base { public override void Run(int value) { Missing(); } }
                    class BrokenSignature : Base { public override int Run(int value) => 1; }
                    """
                ),
            ],
            allowErrors: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var target = CodeType
            .Named("Base", "Product")
            .Member("Run")
            .WithParameters(CodeType.Of<int>());

        var owners = Code
            .Methods.Overriding(target)
            .In(solution)
            .Select(method => method.Symbol!.ContainingType.Name)
            .ToArray();

        Assert.Equal(["BrokenBody"], owners);
    }

    [Fact]
    public void Filtering_preserves_empty_compatible_views_and_definition_counts()
    {
        var workspace = fixture.Workspace();
        var contract = workspace.AddProject("Contracts", [new("I.cs", "public interface I<T> {}")]);
        workspace.AddProject(
            "Product",
            [
                new(
                    "P.cs",
                    "abstract class A : I<int> {} class B : A {} partial class C : I<int>, I<string> {}"
                ),
                new("Partial.cs", "partial class C {}"),
                new("Generated.cs", "class G : I<int> {}", true),
            ],
            framework: "net9.0",
            dependencies: [contract]
        );
        workspace.AddProject(
            "Product",
            [new("P.cs", "abstract class A : I<int> {}")],
            framework: "net10.0",
            dependencies: [contract]
        );
        workspace.AddProject(
            "Tests",
            [new("Fake.cs", "class Fake : I<int> {}")],
            framework: "net9.0",
            isTest: true,
            dependencies: [contract]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var views = Code
            .Interfaces.ImplementationViews()
            .WhereImplementation(implementation => !implementation.Project.IsTestProject)
            .ConcreteOnly();

        var actual = views
            .In(solution)
            .Select(view =>
                $"{view.Interface.Name}:{string.Join(',', view.Implementations.Select(implementation => implementation.Symbol.Name).Order())}"
            )
            .ToArray();
        var counts = views.In(solution).Select(view => view.Implementations.Count).ToArray();

        Assert.Equal(["I:B,C,G", "I:"], actual);
        Assert.Equal([3, 0], counts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructed_contract_evidence_distinguishes_direct_and_inherited_paths(
        bool optimized
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Product",
            [
                new(
                    "P.cs",
                    """
                    interface I<T> {} interface J : I<int> {}
                    abstract class A : I<int> {} class B : A {}
                    class C : I<int>, I<string> {} class D : J {}
                    """
                ),
            ]
        );
        var solution = new AnalysisSolution(
            workspace.Analyze(TestContext.Current.CancellationToken).Projects,
            new AnalysisOptions { EnableOptimizations = optimized },
            TestContext.Current.CancellationToken
        );

        var contracts = Code
            .Interfaces.Where(type => type.Name == "I")
            .ImplementationViews()
            .ConcreteOnly()
            .In(solution)
            .SelectMany(view => view.Implementations)
            .SelectMany(implementation =>
                implementation.Contracts.Select(contract =>
                    $"{implementation.Symbol.Name}:{contract.Symbol}:{contract.IsDirect}"
                )
            )
            .ToArray();

        Assert.Equal(
            ["B:I<int>:False", "C:I<int>:True", "C:I<string>:True", "D:I<int>:False"],
            contracts
        );
    }

    [Fact]
    public void Overrides_retain_ancestor_distance_and_ignore_hiding_members()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Product",
            [
                new(
                    "P.cs",
                    """
                    class A { public virtual void Run(int value) {} }
                    class B : A { public override void Run(int value) {} }
                    class C : B { public override void Run(int value) {} }
                    class D : A { public new void Run(int value) {} }
                    class Other { public virtual void Run(int value) {} }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var target = CodeType
            .Named("A", "Product")
            .Member("Run")
            .WithParameters(CodeType.Of<int>());

        var all = Code
            .Methods.OverrideMatches(target)
            .In(solution)
            .Select(match => $"{match.Method.Symbol!.ContainingType}:{match.Distance}")
            .ToArray();
        var direct = Code
            .Methods.Overriding(target, OverrideSearch.Immediate)
            .In(solution)
            .Select(method => method.Symbol!.ContainingType.Name)
            .ToArray();
        var wrongAssembly = Code
            .Methods.Overriding(CodeType.Named("A", "OtherAssembly").Member("Run"))
            .In(solution);

        Assert.Equal(["B:1", "C:2"], all);
        Assert.Equal(["B"], direct);
        Assert.Empty(wrongAssembly);
    }

    [Fact]
    public void Body_shapes_do_not_conflate_missing_empty_and_return_only_bodies()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Product",
            [
                new(
                    "P.cs",
                    """
                    abstract class A {
                        public abstract void Missing();
                        void Empty() { /* retained comment */ }
                        void Statement() { ; }
                        void Returned() { return; }
                        int Arrow() => 1;
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var shapes = Code
            .Methods.In(solution)
            .Select(method => $"{method.Name}:{method.BodyShape()}")
            .ToArray();
        var empty = Code
            .Methods.WhereBody(MethodBodyShape.EmptyBlock)
            .In(solution)
            .Select(method => method.Name)
            .ToArray();

        Assert.Equal(
            [
                "Missing:Missing",
                "Empty:EmptyBlock",
                "Statement:NonEmptyBlock",
                "Returned:NonEmptyBlock",
                "Arrow:Expression",
            ],
            shapes
        );
        Assert.Equal(["Empty"], empty);
    }
}
