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

    /// <summary>
    /// The ordinary comment that explains the code at a position: a <c>//</c> or <c>/* */</c> comment on its line, or on the
    /// comment line directly above it or above the statement or member it belongs to. Null when there is none.
    /// </summary>
    /// <remarks>Documentation comments describe a member rather than a line, and <c>drillpress-allow</c> comments allow a
    /// finding rather than explain the code, so neither counts. A blank line between a comment and the code separates them.</remarks>
    public static CodeComment? ExplanatoryComment(this AnalysisSource source, int position)
    {
        var token = source.Project.CancellationToken;
        var root = source.Tree.GetRoot(token);
        var text = source.Tree.GetText(token);
        var line = text.Lines.GetLineFromPosition(position);
        var onLine = root.DescendantTrivia(line.Span, descendIntoTrivia: true)
            .FirstOrDefault(trivia =>
                IsExplanation(trivia) && trivia.Span.IntersectsWith(line.Span)
            );
        if (onLine != default)
            return new(source, onLine);
        if (CommentAbove(root, text, line) is { } above)
            return new(source, above);
        return
            Owner(root.FindToken(position, findInsideTrivia: true).Parent) is { } owner
            && text.Lines.GetLineFromPosition(owner.SpanStart) is var ownerLine
            && ownerLine.LineNumber != line.LineNumber
            && CommentAbove(root, text, ownerLine) is { } explanation
            ? new(source, explanation)
            : null;
    }

    /// <summary>The ordinary comment that explains the element's reported line or the statement or member it belongs to.</summary>
    public static CodeComment? ExplanatoryComment(this ICodeElement element) =>
        element.Source.ExplanatoryComment(element.Location.Start);

    /// <summary>Whether an ordinary comment explains the element's reported line or the statement or member it belongs to.</summary>
    public static bool HasExplanatoryComment(this ICodeElement element) =>
        element.ExplanatoryComment() is not null;

    // The statement or member whose first line a comment above explains; blocks and type declarations are too wide.
    private static SyntaxNode? Owner(SyntaxNode? node) =>
        node
            ?.AncestorsAndSelf()
            .FirstOrDefault(ancestor =>
                ancestor is StatementSyntax and not BlockSyntax
                || ancestor
                    is MemberDeclarationSyntax
                        and not BaseTypeDeclarationSyntax
                        and not BaseNamespaceDeclarationSyntax
            );

    private static SyntaxTrivia? CommentAbove(SyntaxNode root, SourceText text, TextLine line)
    {
        if (line.LineNumber == 0)
            return null;
        var above = text.Lines[line.LineNumber - 1];
        for (var position = above.Start; position < above.End; position++)
        {
            if (char.IsWhiteSpace(text[position]))
                continue;
            var trivia = root.FindTrivia(position, findInsideTrivia: true);
            return IsExplanation(trivia) ? trivia : null;
        }
        return null;
    }

    private static bool IsExplanation(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            && !trivia
                .ToString()[2..]
                .TrimStart()
                .StartsWith("drillpress-allow", StringComparison.Ordinal);

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
