namespace DrillPress.Manifest;

/// <summary>Validates responses without Roslyn and produces the shared safe edit plan.</summary>
public sealed class BundleResponseValidator
{
    /// <summary>Requires complete evaluation, checks exact source association, and filters whole conflicting batches.</summary>
    public ValidatedResult Validate(CompilationSnapshot snapshot, BundleResponse response)
    {
        SnapshotValidation.Validate(snapshot);
        ComponentVersion.RequireMatch(response.ProductVersion, "Rule bundle");
        Require(
            response.ProtocolVersion == BundleResponseProtocol.CurrentVersion
                && response.RequestId == snapshot.RequestId,
            "Incompatible bundle response or snapshot request mismatch. Use matching Drill Press components."
        );
        var contexts = snapshot.Projects.ToDictionary(project => project.ContextId);
        Require(
            response.Contexts.Length == contexts.Count
                && response.Contexts.Select(context => context.ContextId).Distinct().Count()
                    == contexts.Count
                && response.Contexts.All(context =>
                    context.IsComplete && contexts.ContainsKey(context.ContextId)
                ),
            "Incomplete context evaluation."
        );
        var documents = snapshot
            .Projects.SelectMany(project => project.Documents)
            .ToDictionary(document => document.DocumentId);
        var files = documents
            .Values.GroupBy(document => document.FileIdentity)
            .ToDictionary(group => group.Key, group => group.First());
        Require(
            response.Batches.All(batch => !string.IsNullOrWhiteSpace(batch.Id))
                && response.Batches.Select(batch => batch.Id).Distinct().Count()
                    == response.Batches.Length,
            "Duplicate or missing batch identity."
        );
        var batches = response.Batches.ToDictionary(batch => batch.Id);
        ValidateFindings(response, contexts, batches);
        var targetFiles = snapshot
            .Projects.Where(project => project.IsAnalysisTarget)
            .SelectMany(project => project.Documents)
            .Select(document => document.FileIdentity)
            .ToHashSet();
        foreach (var batch in batches.Values)
        {
            ValidateBatch(batch, files, contexts, targetFiles);
        }

        var safe = batches
            .Values.Where(batch => IsCommonSafe(batch, contexts))
            .ToDictionary(batch => batch.Id);
        foreach (var id in safe.Keys.ToArray())
        {
            safe[id] = safe.Values.First(batch => SameEdits(batch, safe[id]));
        }

        var findings = Aggregate(response, documents, safe);
        var proposed = findings
            .Where(finding => finding.BatchId is not null)
            .Select(finding => finding.BatchId!)
            .ToHashSet();
        var candidates = safe
            .Values.DistinctBy(batch => batch.Id)
            .Where(batch => proposed.Contains(batch.Id))
            .ToArray();
        var conflicted = FindConflicts(candidates);
        var retained = candidates
            .Where(batch => !conflicted.Contains(batch.Id))
            .OrderBy(batch => batch.Id, StringComparer.Ordinal)
            .ToArray();
        return new ValidatedResult(
            findings
                .Select(finding =>
                    conflicted.Contains(finding.BatchId ?? "")
                        ? finding with
                        {
                            BatchId = null,
                        }
                        : finding
                )
                .ToArray(),
            retained,
            retained
                .SelectMany(batch => batch.Edits)
                .Distinct()
                .OrderBy(edit => edit.FileIdentity, StringComparer.Ordinal)
                .ThenBy(edit => edit.Start)
                .ToArray()
        );
    }

    private static void ValidateFindings(
        BundleResponse response,
        Dictionary<string, ProjectSnapshot> contexts,
        Dictionary<string, FixBatch> batches
    )
    {
        var descriptors = new Dictionary<string, (string Message, RuleFixComplexity? Complexity)>();
        foreach (var context in response.Contexts)
        {
            Require(
                contexts[context.ContextId].IsAnalysisTarget || context.Findings.Length == 0,
                "Finding references a project outside the analysis scope."
            );
            var documents = contexts[context.ContextId]
                .Documents.ToDictionary(document => document.DocumentId);
            foreach (var finding in context.Findings)
            {
                Require(
                    IsSingleLine(finding.RuleId) && IsSingleLine(finding.Message),
                    "Rule identifiers and messages must be non-empty single lines."
                );
                Require(
                    !descriptors.TryGetValue(finding.RuleId, out var descriptor)
                        || descriptor == (finding.Message, finding.FixComplexity),
                    "A rule has inconsistent remediation or fix complexity."
                );
                descriptors[finding.RuleId] = (finding.Message, finding.FixComplexity);
                Require(
                    finding.FixComplexity is not { } complexity || Enum.IsDefined(complexity),
                    "Unknown rule fix complexity."
                );
                Require(
                    finding.Evidence is null || IsSingleLine(finding.Evidence),
                    "Finding evidence must be a non-empty single line."
                );
                Require(
                    documents.TryGetValue(finding.DocumentId, out var document)
                        && !document.IsGenerated,
                    "Finding references an unknown or generated document."
                );
                Require(
                    IsSpan(document!.Text, finding.Start, finding.Length),
                    "Finding span is outside captured source."
                );
                ValidateCoverage(finding, contexts[context.ContextId]);
                Require(
                    finding.BatchId is null || batches.ContainsKey(finding.BatchId),
                    "Finding references an unknown fix batch."
                );
            }
        }
    }

    private static void ValidateCoverage(Finding finding, ProjectSnapshot project)
    {
        foreach (var evidence in finding.Coverage ?? [])
        {
            Require(
                evidence is not null
                    && evidence.ContextId == project.ContextId
                    && evidence.Project == project.Name
                    && evidence.Framework == project.TargetFramework,
                "Coverage evidence belongs to another evaluated context."
            );
            Require(
                Enum.IsDefined(evidence!.State)
                    && Enum.IsDefined(evidence.Metric)
                    && evidence.Reasons is not null
                    && evidence.Reasons.All(Enum.IsDefined),
                "Invalid coverage state or reason."
            );
            Require(
                evidence.Ranges is not null
                    && evidence.Ranges.All(range =>
                        range is not null
                        && Enum.IsDefined(range.State)
                        && project.Documents.Any(document =>
                            document.DocumentId == range.DocumentId
                            && document.Path == range.Path
                            && !document.IsGenerated
                            && IsSpan(document.Text, range.Start, range.Length)
                        )
                    ),
                "Invalid coverage source range."
            );
            Require(
                evidence.State != ExecutionCoverage.Unknown || evidence.Reasons.Count > 0,
                "Unknown coverage requires an explanation."
            );
            Require(
                evidence.Metric == CoverageMetric.Line
                    ? evidence.Lines is not null
                    : evidence.Lines is null,
                "Coverage metric and line counts disagree."
            );
            if (evidence.Lines is { } lines)
                Require(
                    lines.Covered >= 0
                        && lines.Coverable >= lines.Covered
                        && lines.Reasons is not null
                        && lines.Reasons.All(Enum.IsDefined),
                    "Invalid coverage line measurement."
                );
        }
    }

    private static void ValidateBatch(
        FixBatch batch,
        Dictionary<string, DocumentSnapshot> files,
        Dictionary<string, ProjectSnapshot> contexts,
        IReadOnlySet<string> targetFiles
    )
    {
        Require(batch.Edits.Length > 0, "Fix batch contains no edits.");
        Require(
            batch.Validations.Select(validation => validation.ContextId).Distinct().Count()
                == batch.Validations.Length
                && batch.Validations.All(validation => contexts.ContainsKey(validation.ContextId)),
            "Invalid fix validation context."
        );
        foreach (var edit in batch.Edits)
        {
            Require(
                targetFiles.Contains(edit.FileIdentity),
                "Fix targets a document outside the analysis scope."
            );
            Require(
                files.TryGetValue(edit.FileIdentity, out var document)
                    && document.IsEditable
                    && !document.IsGenerated,
                "Fix targets an unknown, generated, or non-editable document."
            );
            Require(
                edit.Fingerprint == document!.Fingerprint
                    && IsSpan(document.Text, edit.Start, edit.Length)
                    && document
                        .Text.AsSpan(edit.Start, edit.Length)
                        .SequenceEqual(edit.OriginalText),
                "Fix does not match captured source identity and span."
            );
        }
    }

    private static bool IsCommonSafe(FixBatch batch, Dictionary<string, ProjectSnapshot> contexts)
    {
        var editedFiles = batch.Edits.Select(edit => edit.FileIdentity).ToHashSet();
        return contexts
            .Values.Where(context =>
                context.Documents.Any(document => editedFiles.Contains(document.FileIdentity))
            )
            .All(context =>
                batch.Validations.Any(validation =>
                    validation.ContextId == context.ContextId && validation.IsSafe
                )
            );
    }

    private static AggregatedFinding[] Aggregate(
        BundleResponse response,
        Dictionary<string, DocumentSnapshot> documents,
        Dictionary<string, FixBatch> safe
    )
    {
        return response
            .Contexts.SelectMany(context => context.Findings)
            .GroupBy(finding =>
                (
                    finding.RuleId,
                    documents[finding.DocumentId].FileIdentity,
                    finding.Start,
                    finding.Length,
                    finding.Message
                )
            )
            .Select(group =>
            {
                var first = group.First();
                var document = documents[first.DocumentId];
                var batch =
                    first.BatchId is { } id
                    && safe.TryGetValue(id, out var proposed)
                    && group.All(finding =>
                        finding.BatchId is { } other
                        && safe.TryGetValue(other, out var alternate)
                        && SameEdits(proposed, alternate)
                    )
                        ? proposed.Id
                        : null;
                var (line, column) = Coordinates(document.Text, first.Start);
                return new AggregatedFinding(
                    first.RuleId,
                    first.Message,
                    document.FileIdentity,
                    document.Path,
                    first.Start,
                    first.Length,
                    line,
                    column,
                    batch
                )
                {
                    Evidence = CombineEvidence(group),
                    FixComplexity = first.FixComplexity,
                    Coverage = group.Any(finding => finding.Coverage is not null)
                        ? group.SelectMany(finding => finding.Coverage ?? []).ToArray()
                        : null,
                };
            })
            .OrderBy(finding => finding.RuleId, StringComparer.Ordinal)
            .ThenBy(finding => finding.Path, StringComparer.Ordinal)
            .ThenBy(finding => finding.Start)
            .ThenBy(finding => finding.Length)
            .ToArray();
    }

    private static string? CombineEvidence(IEnumerable<Finding> findings)
    {
        var evidence = findings
            .Select(finding => finding.Evidence)
            .OfType<string>()
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
        return evidence.Length == 0 ? null : string.Join("; ", evidence);
    }

    private static HashSet<string> FindConflicts(FixBatch[] batches)
    {
        var conflicts = new HashSet<string>();
        for (var left = 0; left < batches.Length; left++)
        {
            for (var right = left; right < batches.Length; right++)
            {
                if (
                    batches[left]
                        .Edits.Any(a => batches[right].Edits.Any(b => a != b && Overlaps(a, b)))
                )
                {
                    conflicts.Add(batches[left].Id);
                    conflicts.Add(batches[right].Id);
                }
            }
        }

        return conflicts;
    }

    private static bool Overlaps(SourceEdit a, SourceEdit b) =>
        a.FileIdentity == b.FileIdentity
        && (
            a.Length == 0 || b.Length == 0
                ? a.Start <= b.Start + b.Length && b.Start <= a.Start + a.Length
                : a.Start < b.Start + b.Length && b.Start < a.Start + a.Length
        );

    private static bool SameEdits(FixBatch a, FixBatch b) => a.Edits.ToHashSet().SetEquals(b.Edits);

    private static bool IsSpan(string text, int start, int length) =>
        start >= 0 && length >= 0 && start <= text.Length && length <= text.Length - start;

    private static bool IsSingleLine(string text) =>
        !string.IsNullOrWhiteSpace(text)
        && !text.Any(char.IsControl)
        && !text.Contains('\u2028')
        && !text.Contains('\u2029');

    private static void Require(
        [System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition,
        string message
    ) => SnapshotValidation.Require(condition, message);

    private static (int Line, int Column) Coordinates(string text, int offset)
    {
        var line = 1;
        var start = 0;
        for (var index = 0; index < offset; index++)
        {
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                if (index + 1 == offset)
                {
                    break;
                }

                index++;
            }

            if (text[index] is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029')
            {
                line++;
                start = index + 1;
            }
        }

        return (line, offset - start + 1);
    }
}
