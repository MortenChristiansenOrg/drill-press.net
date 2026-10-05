using System.Security.Cryptography;
using System.Text;
using DrillPress.Manifest;

namespace DrillPress.Engine;

internal sealed class RuleResponseBuilder
{
    public BundleResponse Build(
        string requestId,
        AnalysisSolution solution,
        IReadOnlyList<RuleDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        var batches = new Dictionary<string, FixBatch>();
        var proposals = new Dictionary<string, List<FixProposal>>();
        var findingBatches = new Dictionary<Finding, List<string>>(
            ReferenceEqualityComparer.Instance
        );
        var findings = solution.Projects.ToDictionary(
            project => project.Snapshot.ContextId,
            _ => new List<Finding>()
        );
        var targetFiles = solution
            .Projects.Where(project => project.Snapshot.IsAnalysisTarget)
            .SelectMany(project => project.Snapshot.Documents)
            .Where(document => !document.IsGenerated)
            .Select(document => document.FileIdentity)
            .ToHashSet();
        foreach (var diagnostic in diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source =
                diagnostic.Source
                ?? throw new InvalidOperationException(
                    "An analyzed diagnostic has no source membership."
                );
            var reportDocument =
                source.Project.Snapshot.Documents.SingleOrDefault(document =>
                    document.Path == diagnostic.Location.FilePath
                )
                ?? throw new InvalidOperationException(
                    "A selected diagnostic location is outside its compilation context."
                );
            var batchIds = new List<string>();
            foreach (
                var proposal in diagnostic.Fixes.Count > 0 ? diagnostic.Fixes
                : diagnostic.Fix is { } single ? [single]
                : []
            )
            {
                if (RegisterProposal(solution, proposal, targetFiles, batches, proposals) is { } id)
                    batchIds.Add(id);
            }

            var finding = new Finding(
                diagnostic.Descriptor.Id,
                diagnostic.Descriptor.Message,
                reportDocument.DocumentId,
                diagnostic.Location.Start,
                diagnostic.Location.Length,
                null
            )
            {
                Evidence = diagnostic.Evidence,
                Disposition = diagnostic.Disposition,
                OutcomeRemediation = diagnostic.OutcomeRemediation,
                Coverage = diagnostic.Coverage.Count == 0 ? null : diagnostic.Coverage,
                FixComplexity = diagnostic.Descriptor.FixComplexity,
            };
            findings[source.Project.Snapshot.ContextId].Add(finding);
            findingBatches.Add(finding, batchIds);
        }

        var combined = CombinedFixValidation.Combine(
            solution,
            batches,
            proposals,
            findingBatches.Values.ToArray()
        );
        foreach (var list in findings.Values)
            for (var index = 0; index < list.Count; index++)
                list[index] = list[index] with
                {
                    BatchId = findingBatches[list[index]]
                        .Select(id => combined.Ids.GetValueOrDefault(id))
                        .FirstOrDefault(id => id is not null),
                };

        return new(
            BundleResponseProtocol.CurrentVersion,
            requestId,
            solution
                .Projects.Select(project => new ContextEvaluation(
                    project.Snapshot.ContextId,
                    true,
                    findings[project.Snapshot.ContextId].ToArray()
                ))
                .ToArray(),
            combined.Batches
        );
    }

    private static string? RegisterProposal(
        AnalysisSolution solution,
        FixProposal proposal,
        HashSet<string> targetFiles,
        Dictionary<string, FixBatch> batches,
        Dictionary<string, List<FixProposal>> proposals
    )
    {
        if (
            proposal.Edits.Count > 0
            && proposal.Edits.All(edit => targetFiles.Contains(edit.FileIdentity))
        )
        {
            var edits = proposal
                .Edits.OrderBy(edit => edit.FileIdentity, StringComparer.Ordinal)
                .ThenBy(edit => edit.Start)
                .ToArray();
            var affected = solution
                .Projects.Where(project =>
                    project.Snapshot.Documents.Any(document =>
                        edits.Any(edit => edit.FileIdentity == document.FileIdentity)
                    )
                )
                .ToArray();
            var validations = affected
                .Select(project => new FixValidation(project.Snapshot.ContextId, false))
                .ToArray();
            if (validations.Length > 0)
            {
                var signature = string.Join(
                    "|",
                    edits.Select(edit =>
                        $"{edit.FileIdentity.Length}:{edit.FileIdentity}{edit.Fingerprint}:{edit.Start}:{edit.Length}:{edit.Replacement.Length}:{edit.Replacement}"
                    )
                );
                var batchId = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(signature))
                );
                batches.TryAdd(batchId, new FixBatch(batchId, edits, validations));
                if (!proposals.TryGetValue(batchId, out var proofs))
                    proposals.Add(batchId, proofs = []);
                proofs.Add(proposal);
                return batchId;
            }
        }

        return null;
    }
}
