using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class CommentQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Comment_candidates_preserve_kinds_text_owners_and_nested_scopes()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    """
                    // file
                    class A
                    {
                        /// <summary>docs</summary>
                        void M()
                        {
                            // outer
                            string text = "// string";
                            /* block */
                            void Local() { // nested
                            }
                            System.Action action = () => { /* lambda */ };
                    #if false
                            // inactive
                    #endif
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var methods = Code.Methods;

        var files = Code.Files.Comments().In(solution);
        var body = methods.Body().Comments().In(solution);
        var nested = methods.Body(NestedFunctions.Include).Comments().In(solution);
        var overlapping = Code.Nodes<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax>()
            .Comments()
            .In(solution);

        Assert.Equal(
            [" file", " <summary>docs</summary>", " outer", " block ", " nested", " lambda "],
            files.Select(comment => comment.Text)
        );
        Assert.Equal(
            [
                CodeCommentKind.SingleLine,
                CodeCommentKind.Documentation,
                CodeCommentKind.SingleLine,
                CodeCommentKind.MultiLine,
                CodeCommentKind.SingleLine,
                CodeCommentKind.MultiLine,
            ],
            files.Select(comment => comment.Kind)
        );
        Assert.Equal([" outer", " block "], body.Select(comment => comment.Text));
        Assert.Equal(
            [" outer", " block ", " nested", " lambda "],
            nested.Select(comment => comment.Text)
        );
        Assert.Equal(
            files.Select(comment => comment.Location),
            overlapping.Select(comment => comment.Location)
        );
        Assert.Equal(
            files.Select(comment => comment.Location),
            Code.Types.Comments().In(solution).Select(comment => comment.Location)
        );
        Assert.Equal(
            files.Select(comment => comment.Location),
            Code.TypeDeclarations.Comments().In(solution).Select(comment => comment.Location)
        );
        Assert.Equal(
            [" <summary>docs</summary>", " outer", " block ", " nested", " lambda "],
            methods.Comments().In(solution).Select(comment => comment.Text)
        );
        Assert.Equal(
            "void M()",
            files[1].ContainingDeclaration!.Syntax.ToString().Split('{')[0].Trim()
        );
        Assert.All(files, comment => Assert.Same(files[0].Source, comment.Source));
    }

    [Theory]
    [InlineData("void M() { /* body */ }", " body ")]
    [InlineData("int M() => /* body */ 1;", " body ")]
    public void Both_body_forms_keep_comments_inside_the_executable_scope(
        string method,
        string expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { " + method + " }")]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var comments = Code.Methods.Body().Comments().In(solution);

        Assert.Equal([expected], comments.Select(comment => comment.Text));
    }

    [Fact]
    public void A_lambda_at_the_body_root_still_obeys_nested_function_policy()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "class A { System.Func<int> M() => () => /* nested */ 1; }")]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var excluded = Code.Methods.Body().Comments().In(solution);
        var included = Code.Methods.Body(NestedFunctions.Include).Comments().In(solution);
        var independent = Code.Methods.Body().NestedBodies().Comments().In(solution);

        Assert.Empty(excluded);
        Assert.Equal([" nested "], included.Select(comment => comment.Text));
        Assert.Equal(
            included.Select(comment => comment.Location),
            independent.Select(comment => comment.Location)
        );
    }

    [Theory]
    [InlineData("/// first\n/// second\nclass A {}", " first\n second")]
    [InlineData("/** docs */ class A {}", " docs ")]
    public void Documentation_delimiters_are_removed(string source, string expected)
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", source)]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var comment = Code.Files.Comments().In(solution).Single();

        Assert.Equal(expected, comment.Text);
        Assert.Equal(CodeCommentKind.Documentation, comment.Kind);
    }
}
