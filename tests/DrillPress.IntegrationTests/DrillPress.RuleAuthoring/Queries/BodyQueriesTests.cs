using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class BodyQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Resolved_body_calls_exclude_error_bound_arguments_but_raw_body_evidence_remains_available()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Bodies",
            [
                new(
                    "Bodies.cs",
                    """
                                class C { void Send(int value) {} void M() { Send(1); int unassigned; Send(unassigned); } }
                    """
                ),
            ],
            allowErrors: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var bodies = Code.Methods.Body();

        var selected = bodies
            .Calls()
            .In(solution)
            .Select(call => call.Operation.Syntax.ToString())
            .ToArray();
        var raw = bodies
            .In(solution)
            .SelectMany(body => body.Calls())
            .Select(call => call.IsResolved)
            .ToArray();

        Assert.Equal(["Send(1)"], selected);
        Assert.Equal([true, false], raw);
    }

    [Fact]
    public void Nested_functions_are_independent_and_arrow_roots_participate()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Bodies",
            [
                new(
                    "Bodies.cs",
                    """
                    abstract class C {
                        void M(bool b) {
                            if (b) {}
                            System.Action lambda = () => { if (b) {} };
                            System.Action anonymous = delegate { if (b) {} };
                            void Local() { if (b) {} }
                        }
                        int Arrow(bool b) => b ? 1 : 2;
                        public abstract void Missing();
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var bodies = Code.Methods.Body();

        var ordinary = bodies
            .ControlFlowNodes(ControlFlowKinds.If | ControlFlowKinds.ConditionalExpression)
            .In(solution)
            .Select(node => node.Syntax.ToString())
            .ToArray();
        var nested = bodies
            .NestedBodies()
            .ControlFlowNodes(ControlFlowKinds.If)
            .In(solution)
            .Select(node => node.Syntax.ToString())
            .ToArray();

        Assert.Equal(["if (b) {}", "b ? 1 : 2"], ordinary);
        Assert.Equal(["if (b) {}", "if (b) {}", "if (b) {}"], nested);
        Assert.Equal(2, bodies.In(solution).Count);
    }

    [Fact]
    public void Overlapping_scopes_report_each_actual_occurrence_once()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Bodies",
            [new("Bodies.cs", "class C { void M() { void Local() { if (true) {} } } }")]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var outer = Code.Methods.Body(NestedFunctions.Include);
        var scopes = CodeQuery<CodeBody>.Create(analysis =>
            outer.In(analysis).Concat(outer.NestedBodies().In(analysis))
        );

        var actual = scopes
            .Nodes<IfStatementSyntax>()
            .In(solution)
            .Select(node => node.Syntax.ToString())
            .ToArray();

        Assert.Equal(["if (true) {}"], actual);
    }

    [Fact]
    public void Generated_methods_follow_the_query_root_boundary()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Bodies",
            [
                new("C.cs", "class C { void M() { if (true) {} } }"),
                new("G.cs", "class G { void M() { if (false) {} } }", true),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var actual = Code
            .Methods.Body()
            .ControlFlowNodes(ControlFlowKinds.If)
            .In(solution)
            .Select(node => node.Syntax.ToString())
            .ToArray();

        Assert.Equal(["if (true) {}"], actual);
    }

    [Fact]
    public void Any_syntax_node_can_scope_a_body()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Bodies",
            [
                new(
                    "Bodies.cs",
                    """
                    class C {
                        static void Send(int value) {}
                        void M() {
                            Send(1);
                            foreach (var item in new[] { 2 }) { Send(item); System.Action later = () => Send(3); }
                        }
                    }
                    """
                ),
            ]
        );
        workspace.AddProject("Other", [new("Other.cs", "class D { }")]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var loop = Code.Nodes<ForEachStatementSyntax>().In(solution).Single();
        var other = solution.Projects[1].Sources[0];

        var excluded = CodeBody.Of(loop.Source, loop.Syntax.Statement).Calls();
        var included = CodeBody
            .Of(loop.Source, loop.Syntax.Statement, NestedFunctions.Include)
            .Calls();

        Assert.Equal(["Send(item)"], excluded.Select(call => call.Operation.Syntax.ToString()));
        Assert.Equal(
            ["Send(item)", "Send(3)"],
            included.Select(call => call.Operation.Syntax.ToString())
        );
        Assert.Throws<ArgumentException>(() => CodeBody.Of(other, loop.Syntax));
    }

    [Fact]
    public void Sections_are_statements_separated_by_blank_lines()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Bodies",
            [
                new(
                    "Bodies.cs",
                    """
                    class C {
                        static int Act(int a, int b) => a + b;
                        static void Check(int value) {}
                        void Test() {
                            var a = 1;
                            // a comment line does not separate
                            var b = 2;

                            var sum = Act(a,

                                b);

                            Check(sum);
                            /* a blank line

                               in a comment does not separate */
                            Check(a);
                        }
                        int Arrow() => 1;
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var methods = Code.Methods.In(solution);

        var sections = methods.Single(method => method.Name == "Test").Body()!.Sections();
        var arrow = methods.Single(method => method.Name == "Arrow").Body()!.Sections();

        Assert.Equal(
            [
                ["var a = 1;", "var b = 2;"],
                ["var sum = Act(a,\n\n            b);"],
                ["Check(sum);", "Check(a);"],
            ],
            sections.Select(section =>
                section.Statements.Select(statement =>
                    statement.ToString().ReplaceLineEndings("\n")
                )
            )
        );
        Assert.Equal([0, 1, 2], sections.Select(section => section.Index));
        Assert.Equal(["Check", "Check"], sections[2].Calls().Select(call => call.Target.Name));
        Assert.Equal(5, sections[0].Location.Line);
        Assert.Empty(arrow);
    }
}
