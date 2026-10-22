using System.Security.Cryptography;
using System.Text;
using DrillPress.Manifest;

namespace DrillPress.Engine;

internal static class CombinedFixValidation
{
    internal static (
        FixBatch[] Batches,
        Dictionary<string, string> Ids,
        Dictionary<string, string> Rejections
    ) Combine(
        AnalysisSolution solution,
        Dictionary<string, FixBatch> batches,
        Dictionary<string, List<FixProposal>> proposals,
        IReadOnlyList<List<string>> reportingGroups
    )
    {
        var candidates = batches.Values.ToArray();
        var conflicted = new HashSet<string>();
        var rejections = new Dictionary<string, string>();
        for (var first = 0; first < candidates.Length; first++)
        for (var second = first + 1; second < candidates.Length; second++)
        {
            var conflict = candidates[first]
                .Edits.SelectMany(left =>
                    candidates[second]
                        .Edits.Where(right => left != right && SourceChanges.Conflict(left, right))
                        .Select(right => (Left: left, Right: right))
                )
                .FirstOrDefault();
            if (conflict.Left is null)
                continue;
            conflicted.Add(candidates[first].Id);
            conflicted.Add(candidates[second].Id);
            rejections.TryAdd(
                candidates[first].Id,
                $"its edit at {Position(solution, conflict.Left)} conflicts with another fix's edit at {Position(solution, conflict.Right)}"
            );
            rejections.TryAdd(
                candidates[second].Id,
                $"its edit at {Position(solution, conflict.Right)} conflicts with another fix's edit at {Position(solution, conflict.Left)}"
            );
        }

        var reportingMemberships = reportingGroups
            .SelectMany((members, index) => members.Select(id => (Id: id, Group: index)))
            .ToLookup(membership => membership.Id, membership => membership.Group);
        var remaining = candidates.Where(batch => !conflicted.Contains(batch.Id)).ToList();
        var result = new List<FixBatch>();
        var ids = new Dictionary<string, string>();
        while (remaining.Count > 0)
        {
            solution.CancellationToken.ThrowIfCancellationRequested();
            var group = new List<FixBatch> { remaining[0] };
            remaining.RemoveAt(0);
            var reachedReportingGroups = reportingMemberships[group[0].Id].ToHashSet();
            var contexts = group[0]
                .Validations.Select(validation => validation.ContextId)
                .ToHashSet();
            for (var index = 0; index < remaining.Count; )
            {
                var next = remaining[index];
                if (
                    !next.Validations.Any(validation => contexts.Contains(validation.ContextId))
                    && !reportingMemberships[next.Id].Any(reachedReportingGroups.Contains)
                )
                {
                    index++;
                    continue;
                }
                group.Add(next);
                reachedReportingGroups.UnionWith(reportingMemberships[next.Id]);
                contexts.UnionWith(next.Validations.Select(validation => validation.ContextId));
                remaining.RemoveAt(index);
                index = 0;
            }

            group = ValidatedGroup(solution, group, proposals, rejections);
            if (group.Count == 0)
                continue;
            contexts = group
                .SelectMany(batch => batch.Validations)
                .Select(validation => validation.ContextId)
                .ToHashSet();
            var edits = Edits(group);
            if (group.Count == 1)
            {
                var single = group[0] with
                {
                    Validations = contexts
                        .Select(context => new FixValidation(context, true))
                        .ToArray(),
                };
                result.Add(single);
                ids.Add(single.Id, single.Id);
                continue;
            }

            // Keep the proved union atomic: a later transport/filter must not select an unproved subset.
            var id = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        string.Join("|", group.Select(batch => batch.Id).Order())
                    )
                )
            );
            result.Add(
                new(
                    id,
                    edits,
                    contexts.Order().Select(context => new FixValidation(context, true)).ToArray()
                )
            );
            foreach (var batch in group)
                ids.Add(batch.Id, id);
        }
        return (result.ToArray(), ids, rejections);
    }

    private static List<FixBatch> ValidatedGroup(
        AnalysisSolution solution,
        List<FixBatch> group,
        Dictionary<string, List<FixProposal>> proposals,
        Dictionary<string, string> rejections
    )
    {
        while (group.Count > 0)
        {
            var edits = Edits(group);
            var reasons = group.ToDictionary(
                batch => batch.Id,
                batch => Check(solution, batch, proposals[batch.Id], edits)
            );
            var accepted = group.Where(batch => reasons[batch.Id] is null).ToList();
            if (accepted.Count == group.Count)
                return group;
            if (accepted.Count > 0)
            {
                foreach (var batch in group.Where(batch => reasons[batch.Id] is not null))
                    rejections[batch.Id] = reasons[batch.Id]!;
                group = accepted;
                continue;
            }
            // A union may fail compilation before any individual proof runs. Isolate invalid proposals once;
            // if all work alone but fail together, withhold the interacting group.
            var individual = group.ToDictionary(
                batch => batch.Id,
                batch => Check(solution, batch, proposals[batch.Id], batch.Edits)
            );
            if (individual.Values.All(reason => reason is null))
            {
                foreach (var batch in group)
                    rejections[batch.Id] =
                        $"safe alone, but not together with {group.Count - 1} other fixes in the same compilation: {reasons[batch.Id]}";
                return [];
            }
            foreach (var batch in group.Where(batch => individual[batch.Id] is not null))
                rejections[batch.Id] = individual[batch.Id]!;
            group = group.Where(batch => individual[batch.Id] is null).ToList();
        }
        return [];
    }

    // The first reason any affected context gives, naming the context when the batch has several.
    private static string? Check(
        AnalysisSolution solution,
        FixBatch batch,
        List<FixProposal> proposals,
        SourceEdit[] edits
    )
    {
        foreach (var validation in batch.Validations)
        {
            var project = solution.Projects.Single(project =>
                project.Snapshot.ContextId == validation.ContextId
            );
            foreach (var proposal in proposals)
                if (proposal.Check(project, edits) is { } reason)
                    return batch.Validations.Length == 1
                        ? reason
                        : $"{project.Snapshot.Name} ({project.Snapshot.TargetFramework}): {reason}";
        }
        return null;
    }

    private static string Position(AnalysisSolution solution, SourceEdit edit) =>
        solution
            .Projects.SelectMany(project => project.Sources)
            .FirstOrDefault(source => source.Document.FileIdentity == edit.FileIdentity)
            is { } source
            ? FixReasons.Position(source, edit.Start)
            : edit.Start.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static SourceEdit[] Edits(IEnumerable<FixBatch> batches) =>
        batches
            .SelectMany(batch => batch.Edits)
            .Distinct()
            .OrderBy(edit => edit.FileIdentity, StringComparer.Ordinal)
            .ThenBy(edit => edit.Start)
            .ToArray();
}
