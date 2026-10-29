using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.Engine;

internal sealed class SourceGenerationSession(
    IFileSystem fileSystem,
    CoverageProcess coverageProcess,
    SourceFilePolicy sourceFiles
)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    internal async Task<GeneratedRuleResult> RunAsync(
        RuleCatalog rules,
        SourceGeneratorCatalog catalog,
        string directory,
        string inputPath,
        string generatedPath,
        AnalysisOptions options,
        CancellationToken cancellationToken
    )
    {
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = shutdown.Token;
        GeneratorProjectCollector? collector = null;
        try
        {
            var transport = new GeneratorTransport(_fileSystem, directory);
            await transport.WriteCapabilitiesAsync(catalog.Descriptions, token);
            var processor = new GeneratorProjectProcessor(
                _fileSystem,
                catalog,
                transport,
                sourceFiles,
                options
            );
            collector = new GeneratorProjectCollector(_fileSystem, transport, processor);
            var collected = await Task.Factory.StartNew(
                () => collector.Collect(inputPath, token),
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            );
            var engine = new AnalysisEngine(_fileSystem, coverageProcess, reuseCompilations: true);
            if (collected.Projects.Length == 0)
            {
                await new CompilationSnapshotFile(_fileSystem).WriteAsync(
                    generatedPath,
                    collected.Snapshot,
                    token
                );
                return new GeneratedRuleResult(
                    collected.Snapshot,
                    await engine.EvaluateAsync(rules, collected.Snapshot, options, token)
                );
            }
            var projects = await Task.WhenAll(collected.Projects);
            await Task.WhenAll(projects.Select(project => project.Preparation.Completion));
            var snapshot = collected.Snapshot with
            {
                Projects = projects
                    .Select(
                        (project, index) =>
                            project.Snapshot with
                            {
                                IsAnalysisTarget = collected
                                    .Snapshot
                                    .Projects[index]
                                    .IsAnalysisTarget,
                            }
                    )
                    .ToArray(),
            };
            snapshot = sourceFiles.Restrict(snapshot, token);
            var contexts = Prepare(engine, projects, snapshot);
            await new CompilationSnapshotFile(_fileSystem).WriteAsync(
                generatedPath,
                snapshot,
                token
            );
            var response = await engine.EvaluateAsync(
                rules,
                snapshot.RequestId,
                contexts,
                options,
                token
            );
            return new GeneratedRuleResult(snapshot, response);
        }
        finally
        {
            await shutdown.CancelAsync();
            if (collector is not null)
                await collector.DrainAsync();
        }
    }

    private static CompilationContext[] Prepare(
        AnalysisEngine engine,
        GeneratedAnalysisContext[] projects,
        CompilationSnapshot snapshot
    )
    {
        var contexts = new List<CompilationContext>();
        for (var index = 0; index < projects.Length; index++)
        {
            var prepared = projects[index].Preparation;
            var updated = snapshot.Projects[index];
            var original = prepared.Project.Snapshot;
            var project =
                original.IsAnalysisTarget == updated.IsAnalysisTarget
                && original
                    .Documents.Zip(updated.Documents)
                    .All(pair => pair.First.IsEditable == pair.Second.IsEditable)
                    ? prepared.Project
                    : prepared.CreateProject(updated);
            engine.ReusePrepared(project);
            contexts.Add(new CompilationContext(updated, project.Compilation));
        }
        return contexts.ToArray();
    }
}
