namespace DrillPress.Manifest;

internal static class RuleFixSelection
{
    public static ValidatedResult Select(
        ValidatedResult result,
        BundleResponse response,
        IReadOnlySet<RuleFixComplexity?>? complexities
    )
    {
        if (complexities is null)
            return result;

        var excludedProposals = response
            .Contexts.SelectMany(context => context.Findings)
            .Where(finding => !complexities.Contains(finding.FixComplexity))
            .Select(finding => finding.BatchId)
            .OfType<string>()
            .ToHashSet();
        var excludedEdits = response
            .Batches.Where(batch => excludedProposals.Contains(batch.Id))
            .SelectMany(batch => batch.Edits)
            .ToHashSet();
        var excludedBatches = result
            .Batches.Where(batch => batch.Edits.Any(excludedEdits.Contains))
            .Select(batch => batch.Id)
            .ToHashSet();
        var findings = result
            .Findings.Where(finding => complexities.Contains(finding.FixComplexity))
            .Select(finding =>
                excludedBatches.Contains(finding.BatchId ?? "")
                    ? finding with
                    {
                        BatchId = null,
                    }
                    : finding
            )
            .ToArray();
        var selectedBatches = findings
            .Select(finding => finding.BatchId)
            .OfType<string>()
            .ToHashSet();
        var batches = result.Batches.Where(batch => selectedBatches.Contains(batch.Id)).ToArray();
        return new(
            findings,
            batches,
            batches
                .SelectMany(batch => batch.Edits)
                .Distinct()
                .OrderBy(edit => edit.FileIdentity, StringComparer.Ordinal)
                .ThenBy(edit => edit.Start)
                .ToArray()
        );
    }
}
