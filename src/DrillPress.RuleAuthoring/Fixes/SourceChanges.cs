using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Fixes;

/// <summary>Builds exact source replacements and complete multi-file proposals. Compilation success is necessary but never sufficient proof of behavioral equivalence.</summary>
public static class SourceChanges
{
    /// <summary>Constructs an original-text-anchored replacement. Eligibility is checked when the proposal is validated.</summary>
    public static SourceEdit Replace(AnalysisSource source, TextSpan span, string replacement) =>
        new(
            source.Document.FileIdentity,
            source.Document.Fingerprint,
            span.Start,
            span.Length,
            source.Tree.GetText(source.Project.CancellationToken).ToString(span),
            replacement
        );

    /// <summary>Builds a proposal that rejects invalid, inactive, generated, conflicting or erroneous source before invoking the required semantic proof in every affected loaded context.</summary>
    public static FixProposal Propose(
        IReadOnlyList<SourceEdit> edits,
        Func<RewriteContext, bool> preservesBehavior
    )
    {
        var batch = edits.ToArray();
        if (batch.Length == 0)
        {
            throw new ArgumentException("A correction requires at least one edit.", nameof(edits));
        }

        return new(batch, project => Validate(project, batch, preservesBehavior));
    }

    private static bool Validate(
        AnalysisProject project,
        SourceEdit[] batch,
        Func<RewriteContext, bool> proof
    )
    {
        var rewritten = project.Compilation;
        var affected = project
            .Sources.Where(source =>
                batch.Any(edit => edit.FileIdentity == source.Document.FileIdentity)
            )
            .ToArray();
        if (
            affected.Length == 0
            || project
                .Compilation.GetDiagnostics(project.CancellationToken)
                .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        )
        {
            return false;
        }

        foreach (var source in affected)
        {
            var edits = batch
                .Where(edit => edit.FileIdentity == source.Document.FileIdentity)
                .OrderBy(edit => edit.Start)
                .ToArray();
            if (!Eligible(source, edits))
            {
                return false;
            }

            var text = source
                .Tree.GetText(project.CancellationToken)
                .WithChanges(
                    edits.Select(edit => new TextChange(
                        new(edit.Start, edit.Length),
                        edit.Replacement
                    ))
                );
            var tree = source.Tree.WithChangedText(text);
            rewritten = rewritten.ReplaceSyntaxTree(source.Tree, tree);
            if (rewritten.Options.SyntaxTreeOptionsProvider is { } provider)
            {
                rewritten = rewritten.WithOptions(
                    rewritten.Options.WithSyntaxTreeOptionsProvider(
                        new RewrittenTreeOptions(provider, source.Tree, tree)
                    )
                );
            }
        }

        return !rewritten
                .GetDiagnostics(project.CancellationToken)
                .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            && proof(new(project, rewritten, Array.AsReadOnly(batch)));
    }

    private static bool Eligible(AnalysisSource source, SourceEdit[] edits)
    {
        if (!source.Document.IsEditable || source.Document.IsGenerated)
        {
            return false;
        }

        var end = -1;
        foreach (var edit in edits)
        {
            if (
                edit.Start < 0
                || edit.Length < 0
                || edit.Start <= end
                || edit.Start > source.Document.Text.Length - edit.Length
                || edit.Fingerprint != source.Document.Fingerprint
                || source.Document.Text.Substring(edit.Start, edit.Length) != edit.OriginalText
            )
            {
                return false;
            }

            var span = new TextSpan(edit.Start, edit.Length);
            if (
                source
                    .Tree.GetRoot()
                    .DescendantTrivia(descendIntoTrivia: true)
                    .Any(trivia =>
                        trivia.RawKind
                            == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.DisabledTextTrivia
                        && trivia.FullSpan.IntersectsWith(span)
                    )
            )
            {
                return false;
            }

            end = edit.Start + edit.Length;
        }

        return true;
    }
}
