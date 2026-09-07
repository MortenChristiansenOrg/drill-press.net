using DrillPress.BundleVerification;

namespace DrillPress.Benchmarks;

public sealed record BenchmarkPlan(
    string RepositoryRoot, string ManagedBundle, string NativeBundle, BundleCase[] Cases);

public sealed record MemorySample(BundleMode Mode, string Case, long PeakMemoryBytes, ProcessOutput Output);

public sealed record ArtifactFile(string Path, long Bytes);

public sealed record ArtifactInventory(BundleMode Mode, long Bytes, ArtifactFile[] Files);
