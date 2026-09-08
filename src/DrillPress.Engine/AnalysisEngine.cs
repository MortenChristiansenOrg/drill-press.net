using System.IO.Abstractions;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

/// <summary>
/// Reconstructs Roslyn compilations from an exported snapshot and presents semantic
/// member references to a compiled rule set.
/// </summary>
public sealed class AnalysisEngine
{
    private readonly IFileSystem _fileSystem;

    /// <summary>Creates an analyzer that reads the snapshot's metadata assemblies from local files.</summary>
    public AnalysisEngine() : this(new FileSystem())
    {
    }

    internal AnalysisEngine(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>Analyzes an in-memory snapshot and returns its deterministically ordered diagnostics.</summary>
    /// <param name="rules">The statically constructed rules to evaluate.</param>
    /// <param name="snapshot">The compilation snapshot to analyze.</param>
    /// <param name="cancellationToken">Stops analysis.</param>
    public async Task<IReadOnlyList<RuleDiagnostic>> AnalyzeAsync(
        RuleSet rules,
        CompilationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var response = await EvaluateAsync(rules, snapshot, cancellationToken);
        return response.Contexts.SelectMany(context => context.Findings.Select(finding =>
        {
            var document = snapshot.Projects.Single(project => project.ContextId == context.ContextId)
                .Documents.Single(document => document.DocumentId == finding.DocumentId);
            var text = SourceText.From(document.Text);
            var position = text.Lines.GetLinePosition(finding.Start);
            return new RuleDiagnostic(new RuleDescriptor(finding.RuleId, finding.Message),
                new SourceLocation(document.Path, finding.Start, finding.Length, position.Line + 1, position.Character + 1));
        })).OrderBy(diagnostic => diagnostic.Descriptor.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.Start).ToArray();
    }

    /// <summary>Evaluates each compilation independently and associates every finding with its source membership.</summary>
    public Task<BundleResponse> EvaluateAsync(RuleSet rules, CompilationSnapshot snapshot, CancellationToken cancellationToken = default) =>
        EvaluateAsync(rules, snapshot, new AnalysisOptions(), cancellationToken);

    /// <summary>Evaluates a snapshot with explicit execution strategy and phase measurements.</summary>
    public async Task<BundleResponse> EvaluateAsync(RuleSet rules, CompilationSnapshot snapshot, AnalysisOptions options, CancellationToken cancellationToken = default)
    {
        CompilationContext[] compilations;
        using (options.Profile.Measure("reconstruction"))
        {
            compilations = Reconstruct(snapshot, cancellationToken);
        }

        return await EvaluateAsync(rules, snapshot.RequestId, compilations, options, cancellationToken);
    }

    /// <summary>Reconstructs the evaluated source graph without requiring dependencies to emit successfully.</summary>
    public CompilationContext[] Reconstruct(CompilationSnapshot snapshot, CancellationToken cancellationToken = default) =>
        new SnapshotCompiler(_fileSystem).Reconstruct(snapshot, cancellationToken);

    /// <summary>Evaluates prepared live or reconstructed contexts, enabling semantic conformance comparisons.</summary>
    public Task<BundleResponse> EvaluateAsync(RuleSet rules, string requestId, IReadOnlyList<CompilationContext> compilations,
        CancellationToken cancellationToken = default) => EvaluateAsync(rules, requestId, compilations, new AnalysisOptions(), cancellationToken);

    /// <summary>Evaluates prepared contexts with the same options used for snapshot-based execution.</summary>
    public Task<BundleResponse> EvaluateAsync(RuleSet rules, string requestId, IReadOnlyList<CompilationContext> compilations,
        AnalysisOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AnalysisSolution solution;
        using (options.Profile.Measure("preparation"))
        {
            solution = new AnalysisSolution(compilations.Select(context => new AnalysisProject(context.Snapshot, context.Compilation, cancellationToken)).ToArray(), options, cancellationToken);
        }

        var diagnostics = rules.Evaluate(solution);
        using var validation = options.Profile.Measure("fix.validation");
        return Task.FromResult(new RuleResponseBuilder().Build(requestId, solution, diagnostics, cancellationToken));
    }
}
