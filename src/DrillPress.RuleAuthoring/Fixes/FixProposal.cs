using DrillPress.Manifest;

namespace DrillPress;

/// <summary>A complete atomic edit batch with a proof evaluated in every affected loaded context.</summary>
/// <param name="edits">All exact replacements required by this correction.</param>
/// <param name="validate">Returns true only when the entire batch is safe in the supplied context.</param>
/// <param name="validateCombined">Optional proof over the complete combined batch. Without it, the proposal cannot combine with other edits in the same compilation.</param>
public sealed class FixProposal(
    IReadOnlyList<SourceEdit> edits,
    Func<AnalysisProject, bool> validate,
    Func<AnalysisProject, IReadOnlyList<SourceEdit>, bool>? validateCombined = null
)
{
    /// <summary>The original-text and byte-identity-anchored replacements.</summary>
    public IReadOnlyList<SourceEdit> Edits { get; } = edits.ToArray();

    /// <summary>Checks binding and source eligibility in an affected context, even if it reported no finding.</summary>
    public bool IsSafeIn(AnalysisProject project) => validate(project);

    /// <summary>Validates this proposal against all edits that will be applied together. Legacy validators accept only their own exact batch.</summary>
    public bool IsSafeIn(AnalysisProject project, IReadOnlyList<SourceEdit> combinedEdits) =>
        validateCombined is not null
            ? validateCombined(project, combinedEdits)
            : Edits.Count == combinedEdits.Count
                && Edits.All(combinedEdits.Contains)
                && validate(project);
}
