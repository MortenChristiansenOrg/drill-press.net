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
    internal BundleResponse EvaluateFixture(
        RuleCatalog rules,
        string requestId,
        IReadOnlyList<CompilationContext> contexts,
        Action<AnalysisSolution> prepare,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solution = new AnalysisSolution(
            contexts
                .Select(context => new AnalysisProject(
                    context.Snapshot,
                    context.Compilation,
                    _fileSystem,
                    cancellationToken
                ))
                .ToArray(),
            cancellationToken
        );
        foreach (var project in solution.Projects)
            project.Coverage.Unavailable(CoverageReason.LooseSource);
        prepare(solution);
        return new RuleResponseBuilder().Build(
            requestId,
            solution,
            rules.Evaluate(solution),
            cancellationToken
        );
    }

    private readonly IFileSystem _fileSystem;
    private readonly CoverageProcess _coverageProcess;
    private readonly CoverageCache _coverageCache;

    /// <summary>Creates an analyzer that reads the snapshot's metadata assemblies from local files.</summary>
    public AnalysisEngine()
        : this(new FileSystem()) { }

    internal AnalysisEngine(IFileSystem fileSystem)
        : this(fileSystem, new CoverageProcess()) { }

    internal AnalysisEngine(IFileSystem fileSystem, CoverageProcess coverageProcess)
        : this(fileSystem, coverageProcess, new CoverageCache(fileSystem)) { }

    internal AnalysisEngine(
        IFileSystem fileSystem,
        CoverageProcess coverageProcess,
        CoverageCache coverageCache
    )
    {
        _fileSystem = fileSystem;
        _coverageProcess = coverageProcess;
        _coverageCache = coverageCache;
    }

    /// <summary>Analyzes an in-memory snapshot and returns its deterministically ordered diagnostics.</summary>
    /// <param name="rules">The statically constructed rules to evaluate.</param>
    /// <param name="snapshot">The compilation snapshot to analyze.</param>
    /// <param name="cancellationToken">Stops analysis.</param>
    public async Task<IReadOnlyList<RuleDiagnostic>> AnalyzeAsync(
        RuleCatalog rules,
        CompilationSnapshot snapshot,
        CancellationToken cancellationToken = default
    )
    {
        var response = await EvaluateAsync(rules, snapshot, cancellationToken);
        return response
            .Contexts.SelectMany(context =>
                context.Findings.Select(finding =>
                {
                    var document = snapshot
                        .Projects.Single(project => project.ContextId == context.ContextId)
                        .Documents.Single(document => document.DocumentId == finding.DocumentId);
                    var text = SourceText.From(document.Text);
                    var position = text.Lines.GetLinePosition(finding.Start);
                    return new RuleDiagnostic(
                        new RuleDescriptor(finding.RuleId, finding.Message)
                        {
                            FixComplexity = finding.FixComplexity,
                        },
                        new SourceLocation(
                            document.Path,
                            finding.Start,
                            finding.Length,
                            position.Line + 1,
                            position.Character + 1
                        )
                    )
                    {
                        Evidence = finding.Evidence,
                        Disposition = finding.Disposition,
                        OutcomeRemediation = finding.OutcomeRemediation,
                        Coverage = finding.Coverage ?? [],
                    };
                })
            )
            .OrderBy(diagnostic => diagnostic.Descriptor.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.Start)
            .ToArray();
    }

    /// <summary>Evaluates each compilation independently and associates every finding with its source membership.</summary>
    public Task<BundleResponse> EvaluateAsync(
        RuleCatalog rules,
        CompilationSnapshot snapshot,
        CancellationToken cancellationToken = default
    ) => EvaluateAsync(rules, snapshot, new AnalysisOptions(), cancellationToken);

    /// <summary>Evaluates a snapshot with explicit execution strategy and phase measurements.</summary>
    public async Task<BundleResponse> EvaluateAsync(
        RuleCatalog rules,
        CompilationSnapshot snapshot,
        AnalysisOptions options,
        CancellationToken cancellationToken = default
    )
    {
        CompilationContext[] compilations;
        using (options.Profile.Measure("reconstruction"))
        {
            compilations = Reconstruct(snapshot, cancellationToken);
        }

        return await EvaluateAsync(
            rules,
            snapshot.RequestId,
            compilations,
            options,
            cancellationToken
        );
    }

    /// <summary>Reconstructs the evaluated source graph without requiring dependencies to emit successfully.</summary>
    public CompilationContext[] Reconstruct(
        CompilationSnapshot snapshot,
        CancellationToken cancellationToken = default
    ) => new SnapshotCompiler(_fileSystem).Reconstruct(snapshot, cancellationToken);

    /// <summary>Evaluates prepared live or reconstructed contexts, enabling semantic conformance comparisons.</summary>
    public Task<BundleResponse> EvaluateAsync(
        RuleCatalog rules,
        string requestId,
        IReadOnlyList<CompilationContext> compilations,
        CancellationToken cancellationToken = default
    ) => EvaluateAsync(rules, requestId, compilations, new AnalysisOptions(), cancellationToken);

    /// <summary>Evaluates prepared contexts with the same options used for snapshot-based execution.</summary>
    public async Task<BundleResponse> EvaluateAsync(
        RuleCatalog rules,
        string requestId,
        IReadOnlyList<CompilationContext> compilations,
        AnalysisOptions options,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        AnalysisSolution solution;
        using (options.Profile.Measure("preparation"))
        {
            solution = new AnalysisSolution(
                compilations
                    .Select(context => new AnalysisProject(
                        context.Snapshot,
                        context.Compilation,
                        _fileSystem,
                        cancellationToken
                    ))
                    .ToArray(),
                options,
                cancellationToken
            );
        }

        if (rules.RequiresCoverage)
        {
            IReadOnlySet<AnalysisProject> selected;
            using (options.Profile.Measure("coverage.planning"))
                selected = rules.CoverageContexts(solution);
            using var collection = options.Profile.Measure("coverage.collection");
            await new CoverageCollector(_fileSystem, _coverageProcess, _coverageCache).PrepareAsync(
                solution,
                selected,
                cancellationToken
            );
        }
        var diagnostics = rules.Evaluate(solution);
        using var validation = options.Profile.Measure("fix.validation");
        return new RuleResponseBuilder().Build(requestId, solution, diagnostics, cancellationToken);
    }
}
