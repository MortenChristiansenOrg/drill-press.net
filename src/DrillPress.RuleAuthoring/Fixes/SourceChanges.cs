using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

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

        return ProposeExplained(
            batch,
            context =>
                preservesBehavior(context) ? null : "the proof rejected the edited compilation"
        );
    }

    /// <summary>Builds a proposal whose proof returns why the batch is unsafe in a context, or null when it is safe.</summary>
    internal static FixProposal ProposeExplained(
        IReadOnlyList<SourceEdit> edits,
        Func<RewriteContext, string?> proof
    )
    {
        var batch = edits.ToArray();
        return new(
            batch,
            (project, combined) =>
                !batch.All(combined.Contains)
                    ? "the combined batch omits some of the proposal's edits"
                    : Validate(project, combined, proof)
        );
    }

    private static string? Validate(
        AnalysisProject project,
        IReadOnlyList<SourceEdit> batch,
        Func<RewriteContext, string?> proof
    )
    {
        var prepared = project.RewriteValidation.Get(batch, edits => Prepare(project, edits));
        return prepared.Context is { } context ? proof(context) : prepared.Reason;
    }

    private static PreparedRewrite Prepare(AnalysisProject project, SourceEdit[] batch)
    {
        var rewritten = project.Compilation;
        var affected = project
            .Sources.Where(source =>
                batch.Any(edit => edit.FileIdentity == source.Document.FileIdentity)
            )
            .ToArray();
        if (affected.Length == 0)
            return new(null, "no edited file belongs to this compilation");
        if (project.RewriteValidation.OriginalHasErrors)
            return new(null, "the original compilation has errors");

        foreach (var source in affected)
        {
            var edits = batch
                .Where(edit => edit.FileIdentity == source.Document.FileIdentity)
                .OrderBy(edit => edit.Start)
                .ThenBy(edit => edit.Length)
                .ToArray();
            if (Ineligible(source, edits) is { } reason)
                return new(null, reason);

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

        var error = rewritten
            .GetDiagnostics(project.CancellationToken)
            .FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return error is null
            ? new(new(project, rewritten, Array.AsReadOnly(batch)), null)
            : new(null, "the edited compilation has an error: " + Describe(error));
    }

    private static string Describe(Diagnostic error)
    {
        var position = error.Location.GetLineSpan();
        var location = position.IsValid
            ? $" at {Path.GetFileName(position.Path)}:{position.StartLinePosition.Line + 1}:{position.StartLinePosition.Character + 1}"
            : "";
        return $"{error.Id}{location} {FixReasons.Text(error.GetMessage(System.Globalization.CultureInfo.InvariantCulture))}";
    }

    private static string? Ineligible(AnalysisSource source, SourceEdit[] edits)
    {
        if (!source.Document.IsEditable || source.Document.IsGenerated)
            return $"{Path.GetFileName(source.Document.Path)} is generated or not editable";

        var disabled = source
            .Tree.GetRoot()
            .DescendantTrivia(descendIntoTrivia: true)
            .Where(trivia =>
                trivia.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.DisabledTextTrivia
            )
            .Select(trivia => trivia.FullSpan)
            .ToArray();
        SourceEdit? previous = null;
        foreach (var edit in edits)
        {
            if (
                edit.Start < 0
                || edit.Length < 0
                || edit.Start > source.Document.Text.Length - edit.Length
                || edit.Fingerprint != source.Document.Fingerprint
                || source.Document.Text.Substring(edit.Start, edit.Length) != edit.OriginalText
            )
                return $"an edit at offset {edit.Start} does not match the captured source";
            if (previous is not null && Conflict(previous, edit))
                return $"edits at {FixReasons.Position(source, previous.Start)} and {FixReasons.Position(source, edit.Start)} overlap, or an insertion touches another edit";
            var span = new TextSpan(edit.Start, edit.Length);
            if (disabled.Any(trivia => trivia.IntersectsWith(span)))
                return $"an edit at {FixReasons.Position(source, edit.Start)} touches inactive preprocessor text";
            previous = edit;
        }

        return null;
    }

    // Edits may share a boundary only when both replace text: an insertion at another edit's boundary has no
    // defined order relative to it. Merge such an insertion into the adjacent replacement instead.
    internal static bool Conflict(SourceEdit first, SourceEdit second) =>
        first.FileIdentity == second.FileIdentity
        && (
            first.Length == 0 || second.Length == 0
                ? first.Start <= second.Start + second.Length
                    && second.Start <= first.Start + first.Length
                : first.Start < second.Start + second.Length
                    && second.Start < first.Start + first.Length
        );
}
