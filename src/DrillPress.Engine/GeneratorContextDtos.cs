using DrillPress.Manifest;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.Engine;

internal sealed record GeneratedAnalysisContext(
    ProjectSnapshot Snapshot,
    CSharpCompilation Live,
    SemanticPreparation Preparation,
    GeneratorReferenceAssembly Assembly,
    ProjectSnapshot[] SourceContexts
);

internal sealed record GeneratorDependency(
    CompilationReferenceSnapshot Reference,
    Task<GeneratedAnalysisContext> Generation
);

internal sealed record CollectedGeneration(
    CompilationSnapshot Snapshot,
    Task<GeneratedAnalysisContext>[] Projects
);

internal sealed record GeneratedRuleResult(CompilationSnapshot Snapshot, BundleResponse Response);
