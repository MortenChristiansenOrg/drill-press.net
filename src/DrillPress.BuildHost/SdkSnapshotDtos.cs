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
    FirstFailureCancellation Cancellation,
    ConcurrentDictionary<ProjectId, string> Identities,
    Dictionary<ProjectId, Task<GeneratedProject>> Generations,
    Dictionary<ProjectId, Task<CompilationContext>> Contexts,
    List<Project> Order
);

/// <summary>A project's default compile glob and the full paths of the sources it selected.</summary>
/// <param name="Glob">The glob's directory and excluded directories.</param>
/// <param name="Items">The full paths of the glob's Compile items without link metadata.</param>
internal sealed record CompileGlobMembership(CompileGlobSnapshot Glob, IReadOnlySet<string> Items);
