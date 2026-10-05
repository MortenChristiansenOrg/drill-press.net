namespace DrillPress;

/// <summary>Controls execution strategy and optional operational measurements without changing rule meaning.</summary>
public sealed record AnalysisOptions
{
    /// <summary>Uses shared candidate constraints and indexes; disable to compare with exhaustive evaluation.</summary>
    public bool EnableOptimizations { get; init; } = true;

    /// <summary>Recollects tests even when source, build, test inputs and environment match cached evidence; use for mutable external test dependencies.</summary>
    public bool RefreshCoverage { get; init; }

    /// <summary>Includes matching coverage ranges in transported diagnostics and opt-in CLI explanations.</summary>
    public bool ExplainCoverage { get; init; }

    /// <summary>Receives phase measurements; omitted profiles are silent.</summary>
    public DrillPress.Manifest.PipelineProfile Profile { get; init; } =
        new(false, TextWriter.Null, "rules");
}
