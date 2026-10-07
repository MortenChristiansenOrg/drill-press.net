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
    public async Task Inconsistent_branches_expose_else_if_and_add_braces_without_recomputing_syntax()
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
        var rules = new RuleCatalog();
        rules
            .Rule("BRACES", "Add braces.")
            .For(
                Code.IfStatements.WithElse()
                    .Where(statement => statement.InconsistentlyBracedBranch is not null)
            )
            .ReportAt(statement => statement.InconsistentlyBracedBranch!)
            .Forbid(fix: statement =>
                Fix.For(statement.InconsistentlyBracedBranch!).AddBraces().Propose()
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
            """.ReplaceLineEndings("\n"),
            result.FixedText("B.cs")
        );
        Assert.Equal([false, true], elseKinds);
    }

    [Fact]
    public async Task Every_unbraced_branch_is_selected_except_else_if_continuations()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Branches",
            [
                new(
                    "B.cs",
                    "class C\n{\n    void M(bool a, bool b)\n    {\n        if (a)\n            if (b)\n                M(b, a);\n        if (a) { } else if (b) M(a, a); else { }\n    }\n}\n"
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("BRACES", "Add braces.")
            .For(Code.IfStatements.Branches().WithoutBraces())
            .Forbid();

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            BRACES Add braces.
            B.cs
              6:13
              7:17
              8:32

            """.ReplaceLineEndings("\n"),
            result.Output
        );
    }
}
