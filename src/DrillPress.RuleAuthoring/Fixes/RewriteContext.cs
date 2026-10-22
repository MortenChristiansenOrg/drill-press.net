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

    private readonly Lazy<Dictionary<string, OrderedEdits>> _edits = new(() => OrderEdits(edits));

    /// <summary>Builds proof evidence for a mapped custom edit without registered retained-input correspondences.</summary>
    public RewriteEvidence? Evidence(AnalysisSource source, SyntaxNode before) =>
        Evidence(source, before, null);

    internal RewriteEvidence? Evidence(
        AnalysisSource source,
        SyntaxNode before,
        IReadOnlyList<SourceEdit>? ownEdits
    ) => Map(source, before) is { } mapped ? new(this, mapped, [], ownEdits) : null;

    /// <summary>Maps an unchanged token through the complete batch, preserving exact kind/text correspondence. Tokens replaced by an edit do not map.</summary>
    public SyntaxToken? MapToken(AnalysisSource source, SyntaxToken before)
    {
        if (
            source.Project != Original
            || before.SyntaxTree != source.Tree
            || Replaces(source, before.Span)
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

    /// <summary>Tells whether an original symbol and a rewritten symbol denote the same entity, such as a source type, member, local or parameter.</summary>
    /// <remarks>Use this instead of <see cref="SymbolEqualityComparer"/>, which never equates source symbols from two compilations. Namespaces and metadata symbols compare by their qualified identity; source symbols must also be declared at the corresponding rewritten declaration.</remarks>
    /// <param name="before">A symbol from <see cref="Original"/>, or null.</param>
    /// <param name="after">A symbol from <see cref="Rewritten"/>, or null.</param>
    public bool Same(ISymbol? before, ISymbol? after) => RewriteSymbols.Same(before, after, this);

    /// <summary>Maps an original span through the complete batch. Edits inside the span change its length; an edit crossing either boundary has no mapping.</summary>
    internal TextSpan? MapSpan(AnalysisSource source, TextSpan span)
    {
        if (!_edits.Value.TryGetValue(source.Document.FileIdentity, out var file))
            return span;
        // Edits are ordered and do not overlap, so their ends are ordered too: everything before the
        // first edit ending after the span start shifts the span, and the following edits are inside or after it.
        var low = FirstEndingAfter(file, span.Start);
        var shift = file.Shifts[low];
        var lengthChange = 0;
        for (var index = low; index < file.Edits.Length; index++)
        {
            var edit = file.Edits[index];
            if (edit.Start >= span.End)
                break;
            if (edit.Start < span.Start || edit.Start + edit.Length > span.End)
                return null;
            lengthChange += edit.Replacement.Length - edit.Length;
        }
        return span.Length + lengthChange >= 0
            ? new TextSpan(span.Start + shift, span.Length + lengthChange)
            : null;
    }

    private bool Replaces(AnalysisSource source, TextSpan span)
    {
        if (!_edits.Value.TryGetValue(source.Document.FileIdentity, out var file))
            return false;
        for (var index = FirstEndingAfter(file, span.Start); index < file.Edits.Length; index++)
        {
            var edit = file.Edits[index];
            if (edit.Start >= span.End)
                return false;
            if (edit.Length > 0)
                return true;
        }
        return false;
    }

    private static int FirstEndingAfter(OrderedEdits file, int position)
    {
        var low = 0;
        var high = file.Edits.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (file.Edits[middle].Start + file.Edits[middle].Length <= position)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private static Dictionary<string, OrderedEdits> OrderEdits(IReadOnlyList<SourceEdit> edits) =>
        edits
            .GroupBy(edit => edit.FileIdentity)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var ordered = group
                        .OrderBy(edit => edit.Start)
                        .ThenBy(edit => edit.Length)
                        .ToArray();
                    var shifts = new int[ordered.Length + 1];
                    for (var index = 0; index < ordered.Length; index++)
                        shifts[index + 1] =
                            shifts[index]
                            + ordered[index].Replacement.Length
                            - ordered[index].Length;
                    return new OrderedEdits(ordered, shifts);
                }
            );

    private sealed record OrderedEdits(SourceEdit[] Edits, int[] Shifts);
}
