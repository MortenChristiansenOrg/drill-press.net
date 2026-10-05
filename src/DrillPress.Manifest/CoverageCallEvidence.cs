namespace DrillPress;

/// <summary>Verified call-site evidence from a matching assembly, portable symbols, and individual collector blocks.</summary>
/// <param name="DocumentId">Original document membership in the evidence context.</param>
/// <param name="Path">Original physical source path.</param>
/// <param name="MethodToken">Metadata token of the compiled containing method.</param>
/// <param name="InstructionOffset">IL offset of the uniquely matched call instruction.</param>
/// <param name="EntryBlock">Module-wide collector index of the block containing the call.</param>
/// <param name="EntryCovered">Whether that block's entry probe was hit.</param>
/// <param name="PrefixCannotThrow">Whether the instructions from block entry to the call cannot prevent its attempt.</param>
/// <param name="CompletionBlock">An optional block reached only after this call returns.</param>
/// <param name="CompletionCovered">Whether the uniquely reached completion block was hit.</param>
public sealed record CoverageCallEvidence(
    string DocumentId,
    string Path,
    int MethodToken,
    int InstructionOffset,
    int EntryBlock,
    bool EntryCovered,
    bool PrefixCannotThrow,
    int? CompletionBlock,
    bool? CompletionCovered
)
{
    /// <summary>Conclusive attempted execution, conclusive skipped execution, or insufficient evidence for this compiled occurrence.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ExecutionCoverage State =>
        !EntryCovered ? ExecutionCoverage.Uncovered
        : PrefixCannotThrow || CompletionCovered == true ? ExecutionCoverage.Covered
        : ExecutionCoverage.Unknown;
}
