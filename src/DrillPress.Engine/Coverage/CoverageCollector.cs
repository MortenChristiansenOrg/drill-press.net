using System.IO.Abstractions;

namespace DrillPress.Engine;

internal sealed class CoverageCollector(
    IFileSystem fileSystem,
    CoverageProcess process,
    CoverageCache cache
)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly CoverageProcess _process = process;
    private readonly CoverageInputs _inputs = new(fileSystem);
    private readonly CoverageCache _cache = cache;
    private readonly CoverageEvaluations _evaluations = new();

    private void RefreshInputs()
    {
        _inputs.Refresh();
        _evaluations.Refresh();
    }

    internal async Task PrepareAsync(
        AnalysisSolution solution,
        IReadOnlySet<AnalysisProject> selected,
        CancellationToken cancellationToken
    )
    {
        var projects = selected
            .Where(project =>
                project.Snapshot.IsAnalysisTarget
                && project.TargetFramework.Length > 0
                && _fileSystem.File.Exists(project.ProjectPath)
            )
            .ToArray();
        foreach (var project in selected.Except(projects))
            project.Coverage.Unavailable(CoverageReason.LooseSource);
        var locks = new List<Stream>();
        try
        {
            foreach (
                var root in projects
                    .Select(_inputs.Root)
                    .Select(root => OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root)
                    .Distinct()
                    .Order(StringComparer.Ordinal)
            )
                locks.Add(await _cache.AcquireAsync(root, cancellationToken));
            if (projects.Length == 0)
                return;
            using var session = new CoverageSession(_fileSystem, _process, _cache);
            foreach (var project in projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await PrepareProjectAsync(
                    project,
                    session,
                    solution.Options.RefreshCoverage,
                    cancellationToken
                );
            }
        }
        finally
        {
            foreach (var cacheLock in locks)
                cacheLock.Dispose();
        }
    }

    private async Task PrepareProjectAsync(
        AnalysisProject project,
        CoverageSession session,
        bool refresh,
        CancellationToken cancellationToken
    )
    {
        var root = _inputs.Root(project);
        var plan = await DiscoverAsync(project, root, cancellationToken);
        if (plan.Tests.Length == 0)
        {
            project.Coverage.Unavailable(CoverageReason.NoApplicableTests);
            return;
        }
        if (await TryApplyCachedAsync(project, plan, root, refresh, cancellationToken))
            return;
        // Evaluate restored imports before capturing the inputs that the test build will consume.
        var revision = session.InputRevision;
        await session.RestoreAsync(project, plan.Tests, cancellationToken);
        if (session.InputRevision != revision)
            RefreshInputs();
        plan = await DiscoverAsync(project, root, cancellationToken);
        if (plan.Tests.Length == 0)
        {
            project.Coverage.Unavailable(CoverageReason.NoApplicableTests);
            return;
        }
        if (await TryApplyCachedAsync(project, plan, root, refresh, cancellationToken))
            return;
        await CollectAndPublishAsync(project, plan, root, session, cancellationToken);
    }

    private async Task<CoveragePlan> DiscoverAsync(
        AnalysisProject project,
        string root,
        CancellationToken cancellationToken
    )
    {
        var paths = _inputs
            .Files(root)
            .Where(path =>
                (
                    _fileSystem.Path.GetExtension(path).ToLowerInvariant()
                    is ".csproj"
                        or ".fsproj"
                        or ".vbproj"
                )
                && !path.Replace('\\', '/').Split('/').Any(component => component is "bin" or "obj")
            )
            .ToArray();
        return await new CoverageDiscovery(_fileSystem, _process, _evaluations).PlanAsync(
            paths,
            project,
            root,
            cancellationToken
        );
    }

    private string[] TrackedInputs(string root, CoveragePlan plan) =>
        _inputs
            .Files(root)
            .Concat(plan.InputPaths)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

    private async Task<bool> TryApplyCachedAsync(
        AnalysisProject project,
        CoveragePlan plan,
        string root,
        bool refresh,
        CancellationToken cancellationToken
    )
    {
        if (refresh)
            return false;
        var path = _cache.ReportPath(
            _inputs.Identity(TrackedInputs(root, plan), project, true, plan, cancellationToken)
        );
        if (!_fileSystem.File.Exists(path))
            return false;
        await ApplyAsync(project, path, root, cancellationToken);
        return true;
    }

    private async Task CollectAndPublishAsync(
        AnalysisProject project,
        CoveragePlan plan,
        string root,
        CoverageSession session,
        CancellationToken cancellationToken
    )
    {
        var sourceIdentity = _inputs.Identity(
            TrackedInputs(root, plan),
            project,
            false,
            plan,
            cancellationToken
        );
        var revision = session.InputRevision;
        var collected = await session.CollectAsync(project, plan.Tests, root, cancellationToken);
        if (session.InputRevision != revision)
            RefreshInputs();
        plan = await DiscoverAsync(project, root, cancellationToken);
        var after = TrackedInputs(root, plan);
        if (sourceIdentity != _inputs.Identity(after, project, false, plan, cancellationToken))
            throw new InvalidOperationException(
                "Coverage inputs changed during test execution; retry analysis."
            );
        await ApplyAsync(project, collected, root, cancellationToken);
        var path = _cache.ReportPath(
            _inputs.Identity(after, project, true, plan, cancellationToken)
        );
        cancellationToken.ThrowIfCancellationRequested();
        _fileSystem.File.Move(collected, path, overwrite: true);
    }

    private async Task ApplyAsync(
        AnalysisProject project,
        string reportPath,
        string root,
        CancellationToken cancellationToken
    )
    {
        var symbols = await new CoverageSymbols(_fileSystem, _process).IdentityAsync(
            project,
            root,
            cancellationToken
        );
        new CoverageReport(_fileSystem).Apply(project, reportPath, symbols);
    }
}
