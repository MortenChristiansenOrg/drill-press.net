using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class InitializerAssignmentTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task An_initializer_assignment_is_added_without_overwriting_a_written_choice()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    "class A { public bool Enabled { get; set; } A M() => new A(); A N() => new A { Enabled = true }; }"
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("INIT", "Disable by default.")
            .For(Code.ObjectCreations.Of(CodeType.Named("A")))
            .Forbid(fix: creation =>
                Fix.For(creation)
                    .AddInitializer("Enabled", "false")
                    .SafeWhen(value => value.Is(false))
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([true, false], result.Findings.Select(finding => finding.HasFix).ToArray());
        Assert.Equal(
            "class A { public bool Enabled { get; set; } A M() => new A() { Enabled = false }; A N() => new A { Enabled = true }; }",
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task A_changed_caller_argument_expression_withholds_the_assignment()
    {
        var workspace = fixture.Workspace();
        const string source = """
            using System.Runtime.CompilerServices;
            class B { public int P { get; init; } }
            class A {
                static string Log(B value, [CallerArgumentExpression("value")] string text = "") => text;
                string M() => Log(new B());
            }
            """;
        workspace.AddProject("A", [new("A.cs", source)]);
        var rules = new RuleCatalog();
        rules
            .Rule("INIT", "Initialize P.")
            .For(Code.ObjectCreations.Of(CodeType.Named("B")))
            .Forbid(fix: creation =>
                Fix.For(creation).AddInitializer("P", "1").SafeWhen(_ => true)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            INIT Initialize P.
            A.cs
              5:23
                fix withheld: `Log(new B())` at 5:19 receives different compiler-supplied arguments, such as a caller line number

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
        Assert.Equal(source, result.FixedText("A.cs"));
    }
}
