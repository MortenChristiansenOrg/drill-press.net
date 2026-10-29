using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.Engine;

internal sealed class GeneratorProjectCollector(
    IFileSystem fileSystem,
    GeneratorTransport transport,
    GeneratorProjectProcessor processor
)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly SnapshotCompiler _compiler = new(fileSystem, revalidateReferences: false);
    private readonly Dictionary<string, GeneratorProjectPlan> _plans = [];
    private readonly Dictionary<string, ProjectSnapshot> _ready = [];
    private readonly Dictionary<string, GeneratorProjectPlan> _pending = [];
    private readonly Dictionary<string, Task<GeneratedAnalysisContext>> _work = [];
    private readonly HashSet<string> _read = [];

    internal async Task DrainAsync()
    {
        Task generation = Task.WhenAll(_work.Values);
        await generation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        var preparation = Task.WhenAll(
            _work
                .Values.Where(task => task.IsCompletedSuccessfully)
                .Select(task => task.Result.Preparation.Completion)
        );
        await preparation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    internal CollectedGeneration Collect(string snapshotPath, CancellationToken cancellationToken)
    {
        CompilationSnapshot? final = null;
        while (final is null || _work.Count < final.Projects.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var failed in _work.Values.Where(task => task.IsFaulted || task.IsCanceled))
                failed.GetAwaiter().GetResult();
            ReadPlans(cancellationToken);
            StartReady(cancellationToken);
            if (final is null && _fileSystem.File.Exists(snapshotPath))
            {
                final = new CompilationSnapshotFile(_fileSystem)
                    .ReadAsync(snapshotPath, cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                // BuildHost writes every plan before the final snapshot, so plans written since
                // the last read are on disk now. Only targets without plans, such as loose sources,
                // take the shortcut below.
                ReadPlans(cancellationToken);
            }
            if (final is not null && _plans.Count == 0)
                return new CollectedGeneration(final, []);
            Thread.Sleep(5);
        }
        if (_work.Count != final.Projects.Length)
            throw new InvalidDataException("Generator contexts do not match the final snapshot.");
        return new CollectedGeneration(
            final,
            final.Projects.Select(project => _work[project.ContextId]).ToArray()
        );
    }

    private void ReadPlans(CancellationToken cancellationToken)
    {
        foreach (var path in transport.Plans())
        {
            if (!_read.Add(path))
                continue;
            var plan = transport.ReadPlanAsync(path, cancellationToken).GetAwaiter().GetResult();
            _plans.Add(plan.Project.ContextId, plan);
            _pending.Add(plan.Project.ContextId, plan);
        }
    }

    private void StartReady(CancellationToken cancellationToken)
    {
        var changed = false;
        var progress = true;
        while (progress)
        {
            progress = false;
            foreach (var plan in _pending.Values.ToArray())
            {
                if (
                    !plan.Project.CompilationReferences.All(reference =>
                        _ready.ContainsKey(reference.ContextId)
                    )
                )
                    continue;
                _ready.Add(plan.Project.ContextId, plan.Project);
                _pending.Remove(plan.Project.ContextId);
                progress = changed = true;
            }
        }
        if (!changed)
            return;
        var contexts = _compiler.Reconstruct(
            CompilationSnapshot.Create(_ready.Values.ToArray()),
            cancellationToken
        );
        foreach (var context in contexts)
        {
            if (_work.ContainsKey(context.Snapshot.ContextId))
                continue;
            var dependencies = context
                .Snapshot.CompilationReferences.Select(reference => new GeneratorDependency(
                    reference,
                    _work[reference.ContextId]
                ))
                .ToArray();
            var plan = _plans[context.Snapshot.ContextId];
            _work.Add(
                context.Snapshot.ContextId,
                Task.Run(
                    () => processor.GenerateAsync(plan, context, dependencies, cancellationToken),
                    CancellationToken.None
                )
            );
        }
    }
}
