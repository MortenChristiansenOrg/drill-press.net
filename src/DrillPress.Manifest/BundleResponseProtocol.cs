using System.Text.Json;

namespace DrillPress.Manifest;

/// <summary>Serializes the internal process response with generated, NativeAOT-compatible metadata.</summary>
public static class BundleResponseProtocol
{
    /// <summary>Exact version understood by the coordinator and bundle.</summary>
    public const int CurrentVersion = 6;

    /// <summary>Produces deterministic UTF-8 JSON without a BOM or trailing newline.</summary>
    public static byte[] Serialize(BundleResponse response) =>
        JsonSerializer.SerializeToUtf8Bytes(
            response,
            CompilationSnapshotJsonContext.Default.BundleResponse
        );

    /// <summary>Rejects invalid JSON, missing members, and responses belonging to another request.</summary>
    /// <param name="json">The complete response from the rule bundle.</param>
    /// <param name="snapshot">The originating captured compilation.</param>
    /// <param name="fixComplexities">Levels to retain after full validation; null selects all, and a null member selects unclassified rules. Batches shared with excluded findings are withheld.</param>
    public static ValidatedResult Read(
        string json,
        CompilationSnapshot snapshot,
        IReadOnlySet<RuleFixComplexity?>? fixComplexities = null
    ) => Read(json, snapshot, fixComplexities, out _);

    internal static ValidatedResult Read(
        string json,
        CompilationSnapshot snapshot,
        IReadOnlySet<RuleFixComplexity?>? fixComplexities,
        out int completeViolationCount
    )
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ComponentVersion.RequireMatch(
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("productVersion", out var productVersion)
            && productVersion.ValueKind == JsonValueKind.String
                ? productVersion.GetString()!
                : "missing",
            "Rule bundle"
        );
        var response =
            root.Deserialize(CompilationSnapshotJsonContext.Default.BundleResponse)
            ?? throw new InvalidDataException("Missing bundle response.");
        var result = new BundleResponseValidator().Validate(snapshot, response);
        completeViolationCount = result.Findings.Count(finding =>
            finding.Disposition == FindingDisposition.Violation
        );
        return RuleFixSelection.Select(result, response, fixComplexities);
    }
}
