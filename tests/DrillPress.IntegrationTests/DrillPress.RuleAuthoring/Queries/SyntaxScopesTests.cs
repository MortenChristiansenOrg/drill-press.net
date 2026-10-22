using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class SyntaxScopesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Blank_lines_exclude_string_tokens_and_nested_functions()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """"
                    class A {
                        void M() {
                            var value = """

                                text
                                """;

                            void Local() {

                            }

                            System.Console.WriteLine(value);
                        }
                    }
                    """"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var body = Code.Methods.Named("M").In(solution).Single().Body()!;

        Assert.Equal(2, SyntaxScopes.BlankLines(body));
        Assert.Same(body.Root, SyntaxScopes.OwnNodes(body.Root, includeSelf: true).First());
        Assert.Equal(
            ["LocalFunctionStatementSyntax"],
            SyntaxScopes
                .OwnNodes(body.Root)
                .Where(SyntaxScopes.IsNestedFunction)
                .Select(node => node.GetType().Name)
                .ToArray()
        );
    }

    [Fact]
    public void Loop_and_member_boundaries_keep_lambdas_distinct()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    using System;
                    class A { void M() { while (true) { Console.WriteLine(1); Action nested = () => Console.WriteLine(2); break; } } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var calls = Code
            .Calls.ToMethodsNamed("WriteLine")
            .In(solution)
            .Select(call =>
                (
                    SyntaxScopes.EnclosingLoop(call.Operation.Syntax)?.GetType().Name,
                    SyntaxScopes.ContainingMember(call.Operation.Syntax)?.GetType().Name,
                    SyntaxScopes.EnclosingType(call.Source, call.Operation.Syntax)?.Name,
                    SyntaxScopes.IsInType(call.Source, call.Operation.Syntax, "A"),
                    SyntaxScopes.IsInType(call.Source, call.Operation.Syntax, CodeType.Named("A")),
                    SyntaxScopes.EnclosingMethod(call.Source, call.Operation.Syntax)?.Name,
                    SyntaxScopes
                        .EnclosingMethod(call.Source, call.Operation.Syntax, skipNested: false)
                        ?.MethodKind.ToString()
                )
            )
            .ToArray();

        Assert.Equal(
            new (string?, string?, string?, bool, bool, string?, string?)[]
            {
                (
                    "WhileStatementSyntax",
                    "MethodDeclarationSyntax",
                    "A",
                    true,
                    true,
                    "M",
                    "Ordinary"
                ),
                (
                    (string?)null,
                    "MethodDeclarationSyntax",
                    "A",
                    true,
                    true,
                    "M",
                    "AnonymousFunction"
                ),
            },
            calls
        );
    }

    [Fact]
    public void Syntax_roots_preserve_order_trivia_and_scoped_query_independence()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Application",
            [new("A.cs", "#nullable disable\nclass A { int x = 1; }")]
        );
        workspace.AddProject("Tests", [new("T.cs", "class T { int x = 2; }")], isTest: true);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var tests = Code.Nodes<LiteralExpressionSyntax>().InTestProjects().In(solution);
        var all = Code.Nodes<LiteralExpressionSyntax>().In(solution);
        var directives = Code
            .Files.Nodes<NullableDirectiveTriviaSyntax>()
            .InNonTestProjects()
            .In(solution);

        Assert.Equal(["2"], tests.Select(node => node.Syntax.ToString()).ToArray());
        Assert.Equal(["1", "2"], all.Select(node => node.Syntax.ToString()).ToArray());
        Assert.Equal(
            ["#nullable disable"],
            directives.Select(node => node.Syntax.ToString()).ToArray()
        );
    }
}
