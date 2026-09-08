using System.Security.Cryptography;
using System.Text;
using DrillPress.Manifest;

namespace DrillPress.Engine;

internal sealed class RuleResponseBuilder
{
    public BundleResponse Build(string requestId, AnalysisSolution solution, IReadOnlyList<RuleDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var batches = new Dictionary<string, FixBatch>();
        var findings = solution.Projects.ToDictionary(project => project.Snapshot.ContextId, _ => new List<Finding>());
        foreach (var diagnostic in diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = diagnostic.Source ?? throw new InvalidOperationException("An analyzed diagnostic has no source membership.");
            var reportDocument = source.Project.Snapshot.Documents.SingleOrDefault(document => document.Path == diagnostic.Location.FilePath)
                ?? throw new InvalidOperationException("A selected diagnostic location is outside its compilation context.");
            string? batchId = null;
            if (diagnostic.Fix is { Edits.Count: > 0 } proposal)
            {
                var edits = proposal.Edits.OrderBy(edit => edit.FileIdentity, StringComparer.Ordinal).ThenBy(edit => edit.Start).ToArray();
                var affected = solution.Projects.Where(project => project.Snapshot.Documents.Any(document =>
                    edits.Any(edit => edit.FileIdentity == document.FileIdentity))).ToArray();
                var validations = affected.Select(project => new FixValidation(project.Snapshot.ContextId, proposal.IsSafeIn(project))).ToArray();
                if (validations.Length > 0 && validations.All(validation => validation.IsSafe))
                {
                    var signature = string.Join("|", edits.Select(edit =>
                        $"{edit.FileIdentity.Length}:{edit.FileIdentity}{edit.Fingerprint}:{edit.Start}:{edit.Length}:{edit.Replacement.Length}:{edit.Replacement}"));
                    batchId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
                    batches.TryAdd(batchId, new FixBatch(batchId, edits, validations));
                }
            }

            findings[source.Project.Snapshot.ContextId].Add(new Finding(diagnostic.Descriptor.Id, diagnostic.Descriptor.Message,
                reportDocument.DocumentId, diagnostic.Location.Start, diagnostic.Location.Length, batchId));
        }

        return new(BundleResponseProtocol.CurrentVersion, requestId,
            solution.Projects.Select(project => new ContextEvaluation(project.Snapshot.ContextId, true,
                findings[project.Snapshot.ContextId].ToArray())).ToArray(), batches.Values.ToArray());
    }
}
