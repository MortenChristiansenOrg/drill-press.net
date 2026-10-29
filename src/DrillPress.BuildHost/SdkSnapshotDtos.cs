using System.Collections.Concurrent;
using DrillPress.Engine;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.BuildHost;

internal sealed record GeneratedProject(
    string ContextId,
    string[] DiagnosticIds,
    EvaluatedMetadata Metadata,
    CSharpCompilation Compilation,
    HashSet<SyntaxTree> Trees,
    CompilationReferenceSnapshot[] Edges,
    ProjectAnalyzers Analyzers,
    Lazy<MetadataReference?> Assembly
);

internal sealed record SourceReference(
    MetadataReference Reference,
    Lazy<MetadataReference?> Assembly
);

internal sealed record GeneratedCompilation(
    CSharpCompilation Compilation,
    HashSet<SyntaxTree> Trees
);

internal sealed record ProjectCaptureState(
    ProjectEvaluator Evaluator,
    CompilerCapture Capture,
    ConcurrentDictionary<ProjectId, string> Identities,
    Dictionary<ProjectId, Task<GeneratedProject>> Generations,
    Dictionary<ProjectId, Task<CompilationContext>> Contexts,
    List<Project> Order
);
