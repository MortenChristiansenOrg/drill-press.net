using DrillPress.Manifest;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.Engine;

/// <summary>A reconstructed or live compilation paired with its exported source memberships.</summary>
/// <param name="Snapshot">Source identities and evaluation metadata.</param>
/// <param name="Compilation">The corresponding compiler semantic model.</param>
public sealed record CompilationContext(ProjectSnapshot Snapshot, CSharpCompilation Compilation);
