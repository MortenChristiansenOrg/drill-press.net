using System.Security.Cryptography;
using System.Text;
using DrillPress.Manifest;

namespace DrillPress.Engine;

internal static class CombinedFixValidation
{
    internal static (FixBatch[] Batches, Dictionary<string, string> Ids) Combine(
        AnalysisSolution solution,
        Dictionary<string, FixBatch> batches,
        Dictionary<string, List<FixProposal>> proposals,
        IReadOnlyList<List<string>> reportingGroups
    )
    {
        var candidates = batches.Values.ToArray();
        var conflicted = new HashSet<string>();
        for (var first = 0; first < candidates.Length; first++)
        for (var second = first + 1; second < candidates.Length; second++)
            if (
                candidates[first]
                    .Edits.Any(left =>
                        candidates[second]
                            .Edits.Any(right =>
                                left != right
                                && left.FileIdentity == right.FileIdentity
                                && left.Start <= right.Start + right.Length
                                && right.Start <= left.Start + left.Length
                            )
                    )
            )
            {
                conflicted.Add(candidates[first].Id);
                conflicted.Add(candidates[second].Id);
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

            group = ValidatedGroup(solution, group, proposals);
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
        return (result.ToArray(), ids);
    }

    private static List<FixBatch> ValidatedGroup(
        AnalysisSolution solution,
        List<FixBatch> group,
        Dictionary<string, List<FixProposal>> proposals
    )
    {
        while (group.Count > 0)
        {
            var edits = Edits(group);
            var accepted = group
                .Where(batch => Validate(solution, batch, proposals[batch.Id], edits))
                .ToList();
            if (accepted.Count == group.Count)
                return group;
            if (accepted.Count > 0)
            {
                group = accepted;
                continue;
            }
            // A union may fail compilation before any individual proof runs. Isolate invalid proposals once;
            // if all work alone but fail together, withhold the interacting group.
            var individual = group
                .Where(batch => Validate(solution, batch, proposals[batch.Id], batch.Edits))
                .ToList();
            if (individual.Count == group.Count)
                return [];
            group = individual;
        }
        return [];
    }

    private static bool Validate(
        AnalysisSolution solution,
        FixBatch batch,
        List<FixProposal> proposals,
        SourceEdit[] edits
    ) =>
        batch.Validations.All(validation =>
            proposals.All(proposal =>
                proposal.IsSafeIn(
                    solution.Projects.Single(project =>
                        project.Snapshot.ContextId == validation.ContextId
                    ),
                    edits
                )
            )
        );

    private static SourceEdit[] Edits(IEnumerable<FixBatch> batches) =>
        batches
            .SelectMany(batch => batch.Edits)
            .Distinct()
            .OrderBy(edit => edit.FileIdentity, StringComparer.Ordinal)
            .ThenBy(edit => edit.Start)
            .ToArray();
}
