using DrillPress.Engine;
using DrillPress.Manifest;

namespace DrillPress.BuildHost;

/// <summary>One export and its live compiler contexts, suitable for independent reconstruction comparisons.</summary>
/// <param name="Snapshot">Ephemeral compiler transport contract.</param>
/// <param name="Contexts">Live compilations with matching source identities.</param>
public sealed record SnapshotExport(CompilationSnapshot Snapshot, CompilationContext[] Contexts);
