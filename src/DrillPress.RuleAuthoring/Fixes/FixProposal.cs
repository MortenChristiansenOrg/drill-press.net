using DrillPress.Manifest;

namespace DrillPress;

/// <summary>A complete atomic edit batch with a proof evaluated in every affected loaded context, or the reason a builder withheld it.</summary>
public sealed class FixProposal
{
    private const string RejectedByValidation =
        "the proposal's validation rejected the edited compilation";

    private readonly Func<AnalysisProject, string?> _validate;
    private readonly Func<AnalysisProject, IReadOnlyList<SourceEdit>, string?> _check;

    /// <summary>Creates a proposal from exact edits and its own validation.</summary>
    /// <param name="edits">All exact replacements required by this correction.</param>
    /// <param name="validate">Returns true only when the entire batch is safe in the supplied context.</param>
    /// <param name="validateCombined">Optional proof over the complete combined batch. Without it, the proposal cannot combine with other edits in the same compilation.</param>
    public FixProposal(
        IReadOnlyList<SourceEdit> edits,
        Func<AnalysisProject, bool> validate,
        Func<AnalysisProject, IReadOnlyList<SourceEdit>, bool>? validateCombined = null
    )
        : this(
            edits,
            project => validate(project) ? null : RejectedByValidation,
            (project, combined) =>
                validateCombined is not null
                    ? validateCombined(project, combined)
                        ? null
                        : RejectedByValidation
                    : combined.Count != edits.Count || !edits.All(combined.Contains)
                        ? "the proposal validates only its own edits, so it cannot combine with other fixes in this compilation"
                        : validate(project)
                            ? null
                            : RejectedByValidation
        ) { }

    internal FixProposal(
        IReadOnlyList<SourceEdit> edits,
        Func<AnalysisProject, IReadOnlyList<SourceEdit>, string?> check
    )
        : this(edits, null, check) { }

    private FixProposal(
        IReadOnlyList<SourceEdit> edits,
        Func<AnalysisProject, string?>? validate,
        Func<AnalysisProject, IReadOnlyList<SourceEdit>, string?> check
    )
    {
        Edits = edits.ToArray();
        _validate = validate ?? (project => check(project, Edits));
        _check = check;
    }

    private FixProposal(string reason)
    {
        Edits = [];
        WithheldReason = reason;
        _validate = _ => reason;
        _check = (_, _) => reason;
    }

    /// <summary>Records that a factory offers no correction and why, such as <c>FixProposal.Withheld("no TimeProvider is in scope")</c>. The finding keeps no fix; <c>drillpress check --explain-fixes</c> and <c>RuleTestResult.ExplainedOutput</c> show the reason.</summary>
    /// <param name="reason">A short single-line explanation without control characters such as tabs.</param>
    public static FixProposal Withheld(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029'))
            throw new ArgumentException(
                "The reason must be a single line without control characters.",
                nameof(reason)
            );
        return new(reason);
    }

    // One atomic proposal from parts that are only safe together, such as both ends of a measurement. Each part's
    // proof runs against the complete batch, and a withheld part withholds the whole.
    internal static FixProposal All(IReadOnlyList<FixProposal> parts) =>
        parts.FirstOrDefault(part => part.Edits.Count == 0) is { } withheld
            ? withheld
            : new(
                parts.SelectMany(part => part.Edits).Distinct().ToArray(),
                (project, combined) =>
                    parts
                        .Select(part => part.Check(project, combined))
                        .FirstOrDefault(reason => reason is not null)
            );

    /// <summary>The original-text and byte-identity-anchored replacements; empty when the proposal was withheld.</summary>
    public IReadOnlyList<SourceEdit> Edits { get; }

    /// <summary>Why a builder or factory withheld this correction before validation, or null for a proposal with edits.</summary>
    public string? WithheldReason { get; }

    /// <summary>Checks binding and source eligibility in an affected context, even if it reported no finding.</summary>
    public bool IsSafeIn(AnalysisProject project) => Edits.Count > 0 && _validate(project) is null;

    /// <summary>Validates this proposal against all edits that will be applied together. Legacy validators accept only their own exact batch.</summary>
    public bool IsSafeIn(AnalysisProject project, IReadOnlyList<SourceEdit> combinedEdits) =>
        Check(project, combinedEdits) is null;

    /// <summary>Returns why the proposal is unsafe with the combined edits in a context, or null when it is safe.</summary>
    internal string? Check(AnalysisProject project, IReadOnlyList<SourceEdit> combinedEdits) =>
        Edits.Count == 0
            ? WithheldReason ?? "the proposal has no edits"
            : _check(project, combinedEdits);
}
