using System.Runtime.ExceptionServices;
using DrillPress.Engine;
using Microsoft.CodeAnalysis;

namespace DrillPress.BuildHost;

/// <summary>
/// Stops an export's remaining contexts once one has failed, without changing the failure it
/// reports: that of the first context in snapshot order whose references were captured.
/// </summary>
/// <remarks>
/// A context that fails while its references were captured fails the same way in any run, so no
/// context after it in snapshot order can be the one reported. Each context has its own cancellation,
/// and that failure cancels only the contexts that neither come before it nor are referenced by one
/// that does. Contexts that fail on their own in a different order therefore cannot cancel the one
/// that is reported.
/// </remarks>
internal sealed class FirstFailureCancellation(CancellationToken cancellationToken) : IDisposable
{
    private readonly CancellationToken _cancellationToken = cancellationToken;
    private readonly Dictionary<ProjectId, CancellationTokenSource> _contexts = [];

    /// <summary>Returns the token that cancels the work of one context.</summary>
    public CancellationToken Register(ProjectId project)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        _contexts.Add(project, source);
        return source.Token;
    }

    /// <summary>
    /// Waits for every context, cancelling those a failure makes irrelevant, and returns them in the
    /// given order or throws the reported failure.
    /// </summary>
    /// <param name="order">Contexts in an order where references precede their dependents.</param>
    /// <param name="contexts">Capture of each context.</param>
    public async Task<CompilationContext[]> CompleteAsync(
        IReadOnlyList<Project> order,
        IReadOnlyDictionary<ProjectId, Task<CompilationContext>> contexts
    )
    {
        var positions = SnapshotOrder(order)
            .Select((project, position) => (project.Id, position))
            .ToDictionary(entry => entry.Id, entry => entry.position);
        var needed = NeededPositions(order, positions);
        await Task.WhenAll(
            order.Select(project => CancelAfterFailureAsync(project, contexts, positions, needed))
        );

        try
        {
            await Task.WhenAll(contexts.Values);
        }
        catch
        {
            ThrowFirstFailure(order, contexts);
            throw;
        }

        return order.Select(project => contexts[project.Id].Result).ToArray();
    }

    /// <summary>
    /// Cancels every context and waits for their work to stop, for an export that fails before it
    /// can complete them.
    /// </summary>
    public async Task CancelAsync(IEnumerable<Task> contexts)
    {
        foreach (var source in _contexts.Values)
            await source.CancelAsync();
        await Task.WhenAll(contexts).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    public void Dispose()
    {
        foreach (var source in _contexts.Values)
            source.Dispose();
    }

    private static IEnumerable<Project> SnapshotOrder(IEnumerable<Project> projects) =>
        projects
            .OrderBy(project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal);

    // The earliest position in snapshot order of a context and every context that references it,
    // directly or not. Dependents follow their references in the order, so walking it backwards
    // settles each context before its references.
    private static Dictionary<ProjectId, int> NeededPositions(
        IReadOnlyList<Project> order,
        Dictionary<ProjectId, int> positions
    )
    {
        var needed = new Dictionary<ProjectId, int>(positions);
        foreach (var project in order.Reverse())
        {
            foreach (var reference in project.ProjectReferences)
            {
                if (needed.TryGetValue(reference.ProjectId, out var position))
                    needed[reference.ProjectId] = Math.Min(position, needed[project.Id]);
            }
        }

        return needed;
    }

    private async Task CancelAfterFailureAsync(
        Project project,
        IReadOnlyDictionary<ProjectId, Task<CompilationContext>> contexts,
        Dictionary<ProjectId, int> positions,
        Dictionary<ProjectId, int> needed
    )
    {
        Task context = contexts[project.Id];
        await context.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!context.IsFaulted)
            return;

        // A context whose reference failed may only repeat that failure; the reference decides.
        Task[] references =
        [
            .. project
                .ProjectReferences.Where(reference => contexts.ContainsKey(reference.ProjectId))
                .Select(reference => contexts[reference.ProjectId]),
        ];
        await Task.WhenAll(references).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!references.All(reference => reference.IsCompletedSuccessfully))
            return;

        foreach (var (id, source) in _contexts)
        {
            if (needed[id] > positions[project.Id])
                await source.CancelAsync();
        }
    }

    // Reports the failure of the first context in snapshot order whose references were captured;
    // a context whose reference failed only repeats that failure. The choice is independent of timing.
    private void ThrowFirstFailure(
        IReadOnlyList<Project> order,
        IReadOnlyDictionary<ProjectId, Task<CompilationContext>> contexts
    )
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var failed = order
            .Where(project => !contexts[project.Id].IsCompletedSuccessfully)
            .Select(project => project.Id)
            .ToHashSet();
        var first = SnapshotOrder(
                order.Where(project =>
                    failed.Contains(project.Id)
                    && !project.ProjectReferences.Any(reference =>
                        failed.Contains(reference.ProjectId)
                    )
                )
            )
            .First();
        if (contexts[first.Id].Exception?.InnerException is { } exception)
            ExceptionDispatchInfo.Throw(exception);
    }
}
