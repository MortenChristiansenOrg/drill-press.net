using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>Removes selected comment trivia while preserving executable tokens and compiler-supplied argument values.</summary>
public sealed class CommentFix
{
    private readonly CodeComment _comment;

    internal CommentFix(CodeComment comment) => _comment = comment;

    /// <summary>Deletes a comment or its entire physical line range when it stands alone, retaining surrounding blank lines. Withholds edits that change parsing or caller information.</summary>
    public FixProposal? Remove()
    {
        var source = _comment.Source;
        if (!source.Document.IsEditable || source.Document.IsGenerated)
            return null;
        var text = source.Tree.GetText(source.Project.CancellationToken);
        var span = _comment.Span;
        var first = text.Lines.GetLineFromPosition(span.Start);
        var last = text.Lines.GetLineFromPosition(span.End);
        var alone =
            string.IsNullOrWhiteSpace(text.ToString(TextSpan.FromBounds(first.Start, span.Start)))
            && string.IsNullOrWhiteSpace(text.ToString(TextSpan.FromBounds(span.End, last.End)));
        var replacement = "";
        if (alone)
            span = TextSpan.FromBounds(first.Start, last.EndIncludingLineBreak);
        else if (
            span.Start > 0
            && span.End < text.Length
            && !char.IsWhiteSpace(text[span.Start - 1])
            && !char.IsWhiteSpace(text[span.End])
        )
            replacement = " ";
        return SourceChanges.Propose(
            [SourceChanges.Replace(source, span, replacement)],
            PreservesCode
        );
    }

    private static bool PreservesCode(RewriteContext context)
    {
        foreach (
            var source in context.Original.Sources.Where(source =>
                context.Edits.Any(edit => edit.FileIdentity == source.Document.FileIdentity)
            )
        )
        {
            var root = source.Tree.GetRoot(source.Project.CancellationToken);
            if (context.TreeFor(source) is not { } tree)
                return false;
            foreach (var token in root.DescendantTokens())
            {
                if (
                    context.Edits.Any(edit =>
                        edit.FileIdentity == source.Document.FileIdentity
                        && new TextSpan(edit.Start, edit.Length).OverlapsWith(token.Span)
                    )
                )
                    continue;
                if (token.Span.Length > 0 && context.MapToken(source, token) is null)
                    return false;
            }
            var evidence = new RewriteEvidence(
                context,
                new(
                    source,
                    root,
                    tree.GetRoot(source.Project.CancellationToken),
                    context.Rewritten.GetSemanticModel(tree)
                ),
                []
            );
            if (RewriteChecks.SameCompilerSuppliedArguments(evidence) != ProofResult.Proven)
                return false;
        }
        return true;
    }
}
