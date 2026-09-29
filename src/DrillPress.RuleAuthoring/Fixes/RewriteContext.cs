using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>The entire edit batch applied to one affected compilation, ready for a consumer's semantic equivalence proof.</summary>
public sealed class RewriteContext(
    AnalysisProject original,
    CSharpCompilation rewritten,
    IReadOnlyList<SourceEdit> edits
)
{
    private readonly Lazy<AnalysisProject> _rewrittenProject = new(() =>
        new(
            original.Snapshot with
            {
                Documents = original
                    .Snapshot.Documents.Zip(
                        rewritten.SyntaxTrees,
                        (document, tree) =>
                            document with
                            {
                                Text = tree.GetText(original.CancellationToken).ToString(),
                                IsEditable = false,
                                Fingerprint = "",
                            }
                    )
                    .ToArray(),
            },
            rewritten,
            original.FileSystem,
            original.CancellationToken
        )
    );

    internal AnalysisSource? RewrittenSource(AnalysisSource source) =>
        TreeFor(source) is { } tree
            ? _rewrittenProject.Value.Sources.FirstOrDefault(candidate => candidate.Tree == tree)
            : null;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        SyntaxTree,
        SemanticModel
    > _models = new();
    private readonly Lazy<Dictionary<SyntaxTree, SyntaxTree>> _trees = new(() =>
    {
        var before = original.Compilation.SyntaxTrees.ToArray();
        var after = rewritten.SyntaxTrees.ToArray();
        return before.Length == after.Length
            ? before
                .Zip(after)
                .Where(pair => pair.First.FilePath == pair.Second.FilePath)
                .ToDictionary(pair => pair.First, pair => pair.Second)
            : [];
    });

    /// <summary>The unmodified source context.</summary>
    public AnalysisProject Original { get; } = original;

    /// <summary>The compilation after all edits belonging to this context, preserving tree options.</summary>
    public CSharpCompilation Rewritten { get; } = rewritten;

    /// <summary>The complete proposed batch, including edits in other contexts.</summary>
    public IReadOnlyList<SourceEdit> Edits { get; } = edits;

    /// <summary>Builds proof evidence for a mapped custom edit without registered retained-input correspondences.</summary>
    public RewriteEvidence? Evidence(AnalysisSource source, SyntaxNode before) =>
        Map(source, before) is { } mapped ? new(this, mapped, []) : null;

    /// <summary>Maps an unchanged token through the complete batch, preserving exact kind/text correspondence. Tokens replaced by an edit do not map.</summary>
    public SyntaxToken? MapToken(AnalysisSource source, SyntaxToken before)
    {
        if (
            source.Project != Original
            || before.SyntaxTree != source.Tree
            || Edits.Any(edit =>
                edit.FileIdentity == source.Document.FileIdentity
                && edit.Length > 0
                && new TextSpan(edit.Start, edit.Length).OverlapsWith(before.Span)
            )
        )
            return null;
        var span = MapSpan(source, before.Span);
        var tree = TreeFor(source);
        if (span is not { } mapped || tree is null || mapped.End > tree.Length)
            return null;
        var after = tree.GetRoot(Original.CancellationToken).FindToken(mapped.Start);
        return after.Span == mapped && after.RawKind == before.RawKind && after.Text == before.Text
            ? after
            : null;
    }

    /// <summary>Maps an original node through the whole batch. Replaced descendants without explicit correspondence and ambiguous spans return null.</summary>
    public NodeRewrite? Map(AnalysisSource source, SyntaxNode before)
    {
        Original.CancellationToken.ThrowIfCancellationRequested();
        if (
            source.Project != Original
            || before.SyntaxTree != source.Tree
            || !Original.Sources.Contains(source)
        )
            return null;
        var tree = TreeFor(source);
        var span = MapSpan(source, before.Span);
        if (tree is null || span is not { } mapped || mapped.End > tree.Length)
            return null;
        var root = tree.GetRoot(Original.CancellationToken);
        var after = root.FindNode(mapped, getInnermostNodeForTie: true);
        if (after.Span != mapped)
            return null;
        var model = _models.GetOrAdd(tree, candidate => Rewritten.GetSemanticModel(candidate));
        return new(source, before, after, model);
    }

    /// <summary>Finds the corresponding rewritten tree by original compilation membership, not an ambiguous filename lookup.</summary>
    public SyntaxTree? TreeFor(AnalysisSource source)
    {
        if (source.Project != Original)
            return null;
        return _trees.Value.GetValueOrDefault(source.Tree);
    }

    private TextSpan? MapSpan(AnalysisSource source, TextSpan span)
    {
        var shift = 0;
        var lengthChange = 0;
        foreach (
            var edit in Edits
                .Where(edit => edit.FileIdentity == source.Document.FileIdentity)
                .OrderBy(edit => edit.Start)
        )
        {
            var end = edit.Start + edit.Length;
            var delta = edit.Replacement.Length - edit.Length;
            if (end <= span.Start)
                shift += delta;
            else if (edit.Start >= span.End)
                continue;
            else if (edit.Start >= span.Start && end <= span.End)
                lengthChange += delta;
            else
                return null;
        }
        return span.Length + lengthChange >= 0
            ? new TextSpan(span.Start + shift, span.Length + lengthChange)
            : null;
    }
}
