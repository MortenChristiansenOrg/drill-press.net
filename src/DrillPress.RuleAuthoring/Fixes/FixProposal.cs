using DrillPress.Manifest;

namespace DrillPress.Fixes;

/// <summary>A complete atomic edit batch with a proof evaluated in every affected loaded context.</summary>
/// <param name="edits">All exact replacements required by this correction.</param>
/// <param name="validate">Returns true only when the entire batch is safe in the supplied context.</param>
public sealed class FixProposal(IReadOnlyList<SourceEdit> edits, Func<AnalysisProject, bool> validate)
{
    /// <summary>The original-text and byte-identity-anchored replacements.</summary>
    public IReadOnlyList<SourceEdit> Edits { get; } = edits.ToArray();

    /// <summary>Checks binding and source eligibility in an affected context, even if it reported no finding.</summary>
    public bool IsSafeIn(AnalysisProject project) => validate(project);
}
