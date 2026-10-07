using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>Source-preserving comment selections over files, declarations and executable bodies.</summary>
public static class CommentQueries
{
    /// <summary>Selects ordinary and documentation comments once per source membership, excluding strings and disabled preprocessor text.</summary>
    public static CodeQuery<CodeComment> Comments(this CodeQuery<CodeFile> files) =>
        Unique(
            files.SelectMany(file =>
                Read(file.Source, file.Source.Tree.GetRoot(file.Source.Project.CancellationToken))
            )
        );

    /// <summary>Selects comments owned by each selected type definition's ordinary declaration.</summary>
    public static CodeQuery<CodeComment> Comments(
        this CodeQuery<CodeTypeDefinition> declarations
    ) =>
        Unique(
            declarations.SelectMany(declaration => Read(declaration.Source, declaration.Syntax))
        );

    /// <summary>Selects comments owned by written type parts, retaining partial source membership.</summary>
    public static CodeQuery<CodeComment> Comments(
        this CodeQuery<CodeTypeDeclaration> declarations
    ) =>
        Unique(
            declarations.SelectMany(declaration => Read(declaration.Source, declaration.Syntax))
        );

    /// <summary>Selects method-owned trivia, including documentation and nested bodies.</summary>
    public static CodeQuery<CodeComment> Comments(this CodeQuery<CodeMethod> methods) =>
        Unique(methods.SelectMany(method => Read(method.Source, method.Syntax)));

    /// <summary>Selects comments in executable scopes while honoring their nested-function policy.</summary>
    public static CodeQuery<CodeComment> Comments(this CodeQuery<CodeBody> bodies) =>
        Unique(bodies.SelectMany(ReadBody));

    /// <summary>Selects trivia owned by arbitrary syntax declarations or scopes, deduplicating overlaps.</summary>
    public static CodeQuery<CodeComment> Comments<TSyntax>(this CodeQuery<CodeNode<TSyntax>> nodes)
        where TSyntax : SyntaxNode =>
        Unique(nodes.SelectMany(node => Read(node.Source, node.Syntax)));

    private static CodeQuery<CodeComment> Unique(CodeQuery<CodeComment> comments) =>
        CodeQuery<CodeComment>.Create(solution =>
            comments.In(solution).DistinctBy(comment => (comment.Source, comment.Span))
        );

    private static IEnumerable<CodeComment> ReadBody(CodeBody body)
    {
        var scope = body.Root.Parent is ArrowExpressionClauseSyntax or LambdaExpressionSyntax
            ? body.Root.Parent
            : body.Root;
        var start = scope switch
        {
            ArrowExpressionClauseSyntax arrow => arrow.ArrowToken.Span.End,
            LambdaExpressionSyntax lambda => lambda.ArrowToken.Span.End,
            _ => body.Root.SpanStart,
        };
        return Read(
            body.Source,
            scope,
            body.Nested,
            TextSpan.FromBounds(start, body.Root.Span.End),
            body.Root.Parent
        );
    }

    private static IEnumerable<CodeComment> Read(
        AnalysisSource source,
        SyntaxNode root,
        NestedFunctions nested = NestedFunctions.Include,
        TextSpan? span = null,
        SyntaxNode? boundary = null
    )
    {
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: false))
        {
            source.Project.CancellationToken.ThrowIfCancellationRequested();
            if (
                trivia.Kind()
                is not (
                    SyntaxKind.SingleLineCommentTrivia
                    or SyntaxKind.MultiLineCommentTrivia
                    or SyntaxKind.SingleLineDocumentationCommentTrivia
                    or SyntaxKind.MultiLineDocumentationCommentTrivia
                )
            )
                continue;
            if (span is { } scopeSpan && !scopeSpan.Contains(trivia.Span))
                continue;
            if (
                nested == NestedFunctions.Exclude
                && trivia
                    .Token.Parent?.AncestorsAndSelf()
                    .TakeWhile(node => node != (boundary ?? root.Parent))
                    .Any(node => CodeBody.IsFunction(node) && node.Span.Contains(trivia.Span))
                    == true
            )
                continue;
            yield return new(source, trivia);
        }
    }
}
