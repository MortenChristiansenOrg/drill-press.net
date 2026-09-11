namespace DrillPress.Analysis;

/// <summary>Controls execution strategy and optional operational measurements without changing rule meaning.</summary>
public sealed record AnalysisOptions
{
    /// <summary>Uses shared candidate constraints and indexes; disable to compare with exhaustive evaluation.</summary>
    public bool EnableOptimizations { get; init; } = true;

    /// <summary>Receives phase measurements; omitted profiles are silent.</summary>
    public DrillPress.Manifest.PipelineProfile Profile { get; init; } =
        new(false, TextWriter.Null, "rules");
}
