using DrillPress.Manifest;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.Fixes;

/// <summary>The entire edit batch applied to one affected compilation, ready for a consumer's semantic equivalence proof.</summary>
public sealed class RewriteContext(
    AnalysisProject original,
    CSharpCompilation rewritten,
    IReadOnlyList<SourceEdit> edits
)
{
    /// <summary>The unmodified source context.</summary>
    public AnalysisProject Original { get; } = original;

    /// <summary>The compilation after all edits belonging to this context, preserving tree options.</summary>
    public CSharpCompilation Rewritten { get; } = rewritten;

    /// <summary>The complete proposed batch, including edits in other contexts.</summary>
    public IReadOnlyList<SourceEdit> Edits { get; } = edits;
}
