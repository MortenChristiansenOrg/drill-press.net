using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Relationships;

public sealed class InterfaceImplementationsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Discovery_retains_test_abstract_inherited_generic_and_generated_definitions(
        bool optimized
    )
    {
        var workspace = fixture.Workspace();
        var contracts = workspace.AddProject(
            "Contracts",
            [new("I.cs", "public interface I<T> { }")]
        );
        workspace.AddProject(
            "Product",
            [
                new(
                    "A.cs",
                    "abstract class A : I<int> { } class B : A { } partial class C : I<int>, I<string> { } interface J : I<int> { }"
                ),
                new("C.cs", "partial class C { }"),
                new("G.g.cs", "struct G : I<int> { }", true),
            ],
            dependencies: [contracts]
        );
        workspace.AddProject(
            "Tests",
            [new("Fake.cs", "class Fake : I<int> { }")],
            isTest: true,
            dependencies: [contracts]
        );
        var solution = new AnalysisSolution(
            workspace.Analyze(TestContext.Current.CancellationToken).Projects,
            new AnalysisOptions { EnableOptimizations = optimized },
            TestContext.Current.CancellationToken
        );
        var contract = solution.Types.Single(type => type.Name == "I");

        var views = solution.Implementations.In(contract);
        var repeated = solution.Implementations.In(contract);

        Assert.Same(views, repeated);
        Assert.Equal(
            ["Contracts", "Product", "Tests"],
            Assert.Single(views).Projects.Select(project => project.Name).Order()
        );
        Assert.Equal(
            [
                ("Product", "A", true, false),
                ("Product", "B", false, false),
                ("Product", "C", false, false),
                ("Product", "J", true, false),
                ("Product", "G", false, false),
                ("Tests", "Fake", false, true),
            ],
            views
                .Single()
                .Implementations.Select(item =>
                    (
                        item.Project.Name,
                        item.Symbol.Name,
                        item.Symbol.IsAbstract,
                        item.Project.IsTestProject
                    )
                )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Alternate_dependency_contexts_have_separate_implementation_views(bool optimized)
    {
        var workspace = fixture.Workspace();
        var contract = workspace.AddProject("Contracts", [new("I.cs", "public interface I { }")]);
        workspace.AddProject(
            "Product",
            [new("A.cs", "class A : I { }")],
            framework: "net9.0",
            dependencies: [contract]
        );
        workspace.AddProject(
            "Product",
            [new("B.cs", "class B : I { }")],
            framework: "net10.0",
            dependencies: [contract]
        );
        var solution = new AnalysisSolution(
            workspace.Analyze(TestContext.Current.CancellationToken).Projects,
            new AnalysisOptions { EnableOptimizations = optimized },
            TestContext.Current.CancellationToken
        );

        var views = solution.Implementations.In(solution.Types.Single(type => type.Name == "I"));

        Assert.Equal(
            ["net9.0:A", "net10.0:B"],
            views.Select(view =>
                string.Join(
                    ",",
                    view.Implementations.Select(item =>
                        item.Project.TargetFramework + ":" + item.Symbol.Name
                    )
                )
            )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equal_assembly_names_do_not_merge_unrelated_contracts(bool optimized)
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Contracts",
            [new("I.cs", "public interface I { }")],
            framework: "net9.0"
        );
        var second = workspace.AddProject(
            "Contracts",
            [new("I.cs", "public interface I { }")],
            framework: "net10.0"
        );
        workspace.AddProject("Consumer", [new("A.cs", "class A : I { }")], dependencies: [second]);
        var solution = new AnalysisSolution(
            workspace.Analyze(TestContext.Current.CancellationToken).Projects,
            new AnalysisOptions { EnableOptimizations = optimized },
            TestContext.Current.CancellationToken
        );

        var implementations = solution
            .Types.Where(type => type.Name == "I")
            .Select(type =>
                string.Join(
                    ",",
                    solution
                        .Implementations.In(type)
                        .SelectMany(view => view.Implementations)
                        .Select(item => item.Symbol.Name)
                )
            )
            .ToArray();

        Assert.Equal(["", "A"], implementations);
    }
}
