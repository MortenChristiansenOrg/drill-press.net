using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.Engine;

/// <summary>
/// Creates the bundle's rules once the snapshot, and any accepted baseline, is available, restricted to the
/// coordinator's rule selection and reporting exclusions.
/// </summary>
internal sealed class RuleCatalogSource(
    IFileSystem fileSystem,
    Func<SourceBaseline?, RuleCatalog> create,
    string? acceptedSourcesPath,
    RuleSelection selection
)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    internal async Task<RuleCatalog> CreateAsync(
        CompilationSnapshot current,
        AnalysisOptions options,
        CancellationToken cancellationToken
    )
    {
        if (acceptedSourcesPath is null)
            return create(null).Restrict(selection);
        using var measure = options.Profile.Measure("baseline");
        var accepted = await new AcceptedSourcesFile(_fileSystem).ReadAsync(
            acceptedSourcesPath,
            cancellationToken
        );
        var contexts = new SnapshotCompiler(_fileSystem).Reconstruct(
            AcceptedSourceProjection.Project(current, accepted),
            cancellationToken
        );
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
        return create(new SourceBaseline(solution)).Restrict(selection);
    }
}
