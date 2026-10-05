using System.Globalization;

namespace DrillPress.Manifest;

/// <summary>Formats coverage evidence without exposing collector-specific report models.</summary>
public static class CoverageEvidenceFormatter
{
    /// <summary>Renders compact state, reason codes, and measured counts; an optional line threshold adds the required percentage.</summary>
    public static string Format(CoverageEvidence evidence, double? minimum = null)
    {
        var reasons = string.Join(", ", evidence.Reasons.Select(Code));
        if (evidence.Lines is { } lines)
        {
            var counts = $"{lines.Covered}/{lines.Coverable}";
            return lines.IsComplete && lines.Coverable > 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"line coverage: {lines.Percentage:0.##}% ({counts}){(minimum is { } threshold ? string.Create(CultureInfo.InvariantCulture, $", required {threshold:0.##}%") : "")}"
                )
                : $"line coverage: unknown ({counts}; {reasons})";
        }
        var state = evidence.State.ToString().ToLowerInvariant();
        return reasons.Length == 0 ? $"coverage: {state}" : $"coverage: {state} ({reasons})";
    }

    /// <summary>Returns actionable remediation for a typed reason, including cases where adding tests cannot fix the current mapping.</summary>
    public static string Explain(CoverageReason reason) =>
        reason switch
        {
            CoverageReason.EvidenceNotPrepared =>
                "Evaluate through AnalysisEngine or an executable bundle to prepare test evidence.",
            CoverageReason.LooseSource =>
                "Analyze a restored on-disk project; loose or synthetic sources have no collectable test identity.",
            CoverageReason.NoApplicableTests =>
                "Add or locate a test project referencing this target below the discovery root.",
            CoverageReason.MissingSymbols =>
                "Build with associated portable symbols and refresh coverage.",
            CoverageReason.SymbolIdentityMismatch =>
                "Rebuild the matching target/framework with captured compiler settings, then refresh coverage.",
            CoverageReason.ModuleIdentityMismatch =>
                "Check the selected build output and report module identity, then refresh coverage.",
            CoverageReason.SourceIdentityMismatch =>
                "Capture the current source and rebuild before refreshing coverage.",
            CoverageReason.MissingOrExcludedDocument =>
                "Check collector exclusions and whether tests load this document's assembly; refresh after correcting them.",
            CoverageReason.MissingRange =>
                "The report has no execution point for this occurrence; inspect its source shape and exclusions.",
            CoverageReason.PartialRange =>
                "Matching ranges are inconclusive; complementary test paths and refreshed collection may help.",
            CoverageReason.UnsupportedExpressionMapping =>
                "The current mapping cannot distinguish this expression; additional tests alone may not resolve it.",
            CoverageReason.IncompleteLineEvidence =>
                "Some selected documents have no usable evidence; inspect their individual reasons.",
            CoverageReason.ZeroCoverableLines =>
                "No coverable lines were measured; explicitly exclude scopes outside the policy rather than granting vacuous success.",
            CoverageReason.InvalidReportRange =>
                "The collector range has invalid coordinates or function identity; regenerate the report.",
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    private static string Code(CoverageReason reason) =>
        reason switch
        {
            CoverageReason.EvidenceNotPrepared => "not-prepared",
            CoverageReason.LooseSource => "loose-source",
            CoverageReason.NoApplicableTests => "no-tests",
            CoverageReason.MissingSymbols => "missing-symbols",
            CoverageReason.SymbolIdentityMismatch => "symbol-mismatch",
            CoverageReason.ModuleIdentityMismatch => "module-mismatch",
            CoverageReason.SourceIdentityMismatch => "source-mismatch",
            CoverageReason.MissingOrExcludedDocument => "missing-or-excluded-document",
            CoverageReason.MissingRange => "missing-range",
            CoverageReason.PartialRange => "partial-range",
            CoverageReason.UnsupportedExpressionMapping => "unsupported-mapping",
            CoverageReason.IncompleteLineEvidence => "incomplete-lines",
            CoverageReason.ZeroCoverableLines => "zero-coverable-lines",
            CoverageReason.InvalidReportRange => "invalid-report-range",
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };
}
