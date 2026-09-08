namespace DrillPress.Manifest;

/// <summary>Identifies which stage stopped a guarded fix operation.</summary>
public enum FixApplicationOutcome
{
    /// <summary>All prepared replacements completed.</summary>
    Completed,
    /// <summary>Validation or preparation failed before any replacement.</summary>
    PreparationFailed,
    /// <summary>A replacement failed; earlier replacements remain in place.</summary>
    CommitFailed,
    /// <summary>Cancellation stopped preparation or further replacements.</summary>
    Cancelled,
}

/// <summary>Recovery information for a single application of a validated plan.</summary>
/// <param name="Outcome">Completion or failure stage.</param>
/// <param name="Changed">Paths already replaced, in commit order.</param>
/// <param name="Failed">Path being processed when failure occurred, when known.</param>
/// <param name="Pending">Other paths not replaced.</param>
/// <param name="Error">Operational failure description, or null on success.</param>
public sealed record FixApplicationResult(FixApplicationOutcome Outcome, string[] Changed, string? Failed, string[] Pending, string? Error);

internal sealed record PreparedSourceFile(DocumentSnapshot Document, string Path, string TemporaryPath,
    PhysicalFileIdentity Identity, byte[] OriginalBytes, byte[] ReplacementBytes)
{
    internal bool TemporaryCreated { get; set; }
}
