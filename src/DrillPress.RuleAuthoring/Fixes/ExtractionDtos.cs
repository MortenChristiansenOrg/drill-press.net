using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>How a new private extraction member handles existing or inherited member names.</summary>
public enum ExtractionNameCollision
{
    /// <summary>Keep the diagnostic without a correction when the requested name is already used.</summary>
    Refuse,

    /// <summary>Try deterministic numeric suffixes, starting at 1, up to a bounded 1000 candidates.</summary>
    AddNumericSuffix,
}

/// <summary>Evidence after the complete atomic extraction and any other accepted edits in this context.</summary>
/// <param name="Context">The actual affected context, including contexts without an original finding.</param>
/// <param name="BeforeOwner">The original containing source type.</param>
/// <param name="AfterOwner">The corresponding type after all edits.</param>
/// <param name="Member">The exact constant or one-parameter helper used by every replacement.</param>
/// <param name="Reused">Whether an existing constant was reused instead of inserting a member.</param>
/// <param name="Occurrences">All selected occurrences present in this context, with retained capture mappings.</param>
public sealed record ExtractionEvidence(
    RewriteContext Context,
    INamedTypeSymbol BeforeOwner,
    INamedTypeSymbol AfterOwner,
    ISymbol Member,
    bool Reused,
    IReadOnlyList<RewriteEvidence> Occurrences
);

/// <summary>Policies for deriving an extracted helper's parameter name.</summary>
public enum ParameterName
{
    /// <summary>Camel-case the representative bound member/local/parameter name, falling back to value for invalid identifiers.</summary>
    FromCapture,
}
