using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class StyleQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Type_parts_keep_their_own_modifiers_paths_and_locations()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Types",
            [
                new("A.cs", "partial class C { internal class Nested {} }"),
                new("Parts/B.cs", "internal partial class C {} internal delegate void Handler();"),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var parts = Code
            .TypeDeclarations.TopLevel()
            .WithExplicitModifier(Modifier.Internal)
            .InFolder("Parts")
            .In(solution)
            .Select(type => $"{type.Name}:{type.Source.Document.Path}")
            .ToArray();
        var definitions = Code.Types.In(solution).Select(type => type.Name).ToArray();

        Assert.Equal(["C:Parts/B.cs", "Handler:Parts/B.cs"], parts);
        Assert.Equal(["C", "Nested", "Handler"], definitions);
    }

    [Fact]
    public void Explicit_types_distinguish_real_var_types_and_preserve_local_groups()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Declarations",
            [
                new(
                    "D.cs",
                    """
                    namespace Explicit { class var {}
                    class C { static bool Try(out int value) { value = 1; return true; }
                        void M(int[] values) { int a = 1, b = 2; var typed = new var();
                            foreach (int value in values) {} Try(out int result);
                        }
                    }
                    } class D { void M() { var inferred = 1; } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var groups = Code
            .LocalVariables.WithExplicitType()
            .WithInitializer()
            .In(solution)
            .Select(local => $"{local.TypeName.Syntax}:{local.Variables.Count}")
            .ToArray();
        var loops = Code
            .ForEachLoops.WithExplicitType()
            .In(solution)
            .Select(loop => loop.TypeName.Syntax.ToString())
            .ToArray();
        var output = Code
            .OutVariables.WithExplicitType()
            .In(solution)
            .Select(local => local.TypeName.Syntax.ToString())
            .ToArray();

        Assert.Equal(["int:2", "var:1"], groups);
        Assert.Equal(["int"], loops);
        Assert.Equal(["int"], output);
    }

    [Fact]
    public async Task Branches_expose_else_if_and_add_braces_without_recomputing_syntax()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Branches",
            [
                new(
                    "B.cs",
                    "class C { void M(bool flag) { if (flag) { } else M(false); if (flag) {} else if (flag) {} } }"
                ),
            ]
        );
        var rules = new RuleSet();
        rules
            .For(
                Code.IfStatements.WithElse()
                    .Where(statement => statement.BranchWithoutBraces is not null)
            )
            .Forbid(
                "BRACES",
                "Add braces.",
                at: statement => statement.BranchWithoutBraces!,
                fix: statement => Fix.For(statement.BranchWithoutBraces!).AddBraces().Propose()
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);
        var elseKinds = Code
            .IfStatements.WithElse()
            .In(workspace.Analyze(TestContext.Current.CancellationToken))
            .Select(statement => statement.Else!.IsElseIf)
            .ToArray();

        Assert.Equal(
            [new TestFinding("BRACES", "B.cs", 1, 50, "M(false);", true)],
            result.Findings
        );
        Assert.Equal(
            """
            class C { void M(bool flag) { if (flag) { } else {
                M(false);
            } if (flag) {} else if (flag) {} } }
            """,
            result.FixedText("B.cs")
        );
        Assert.Equal([false, true], elseKinds);
    }
}
