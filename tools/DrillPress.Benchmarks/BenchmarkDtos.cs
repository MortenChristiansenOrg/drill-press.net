using DrillPress.BundleVerification;

namespace DrillPress.Benchmarks;

public sealed record BenchmarkPlan(
    string RepositoryRoot,
    string ManagedBundle,
    string NativeBundle,
    BundleCase[] Cases
);

public sealed record MemorySample(
    BundleMode Mode,
    string Case,
    long PeakMemoryBytes,
    ProcessOutput Output
);

public sealed record ArtifactFile(string Path, long Bytes);

public sealed record ArtifactInventory(BundleMode Mode, long Bytes, ArtifactFile[] Files);

public sealed record ProcessMeasurement(
    double WallMilliseconds,
    double UserCpuMilliseconds,
    double SystemCpuMilliseconds,
    long PeakResidentBytes,
    string CpuScope,
    string MemoryScope
);

public sealed record MeasuredExecution(
    string Executable,
    string[] Arguments,
    string WorkingDirectory,
    int ExitCode,
    string StandardOutputPath,
    string StandardErrorPath,
    ProcessMeasurement Measurement
);

public sealed record ResultSignatureFiles(
    string ResponsePath,
    string PlanPath,
    string PublicOutputPath,
    string ResponseHash,
    string PlanHash,
    string PublicOutputHash,
    int ContextFindings,
    int ActionableLocations,
    int SafeBatches,
    int SafeEdits,
    long PublicOutputBytes
);

public sealed record RepositoryOperation(
    string Name,
    MeasuredExecution Execution,
    DrillPress.Manifest.ProfileEvent[] Profile
);

public sealed record RepositoryWorkload(string Name, string Root, string Target);

public sealed record RepositoryVariant(string Name, string Bundle, bool Native, bool Optimized);

public sealed record RepositoryRun(
    string Workload,
    int Repetition,
    string Variant,
    long SnapshotBytes,
    ResultSignatureFiles Initial,
    ResultSignatureFiles? Rechecked
);

public sealed record BundleIdentity(string EntryPoint, ArtifactFingerprint[] Files);

public sealed record ArtifactFingerprint(string Path, long Bytes, string Sha256);
