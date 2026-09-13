using System.IO.Abstractions;

namespace DrillPress.Manifest;

/// <summary>Applies one complete protocol plan using prepared, individually atomic file replacements.</summary>
public sealed class FixPlanApplier
{
    private readonly IFileSystem _fileSystem;
    private readonly SourceFilePolicy _policy;
    private readonly AtomicFileReplacer _replacer;

    /// <summary>Creates an applier for local source files and OS identity checks.</summary>
    public FixPlanApplier()
        : this(new FileSystem(), new FileIdentityProbe()) { }

    internal FixPlanApplier(
        IFileSystem fileSystem,
        FileIdentityProbe probe,
        AtomicFileReplacer? replacer = null
    )
    {
        _fileSystem = fileSystem;
        _policy = new SourceFilePolicy(fileSystem, probe);
        _replacer = replacer ?? new AtomicFileReplacer(fileSystem, _policy);
    }

    /// <summary>
    /// Validates the exact bundle response, prepares every output, then replaces files once.
    /// Concurrent writers must be stopped: verification and replacement cannot form a cross-process transaction.
    /// Earlier successful replacements are retained on failure; unused temporary files are removed.
    /// </summary>
    public async Task<FixApplicationResult> ApplyAsync(
        CompilationSnapshot snapshot,
        string response,
        CancellationToken cancellationToken = default,
        PipelineProfile? profile = null
    )
    {
        profile ??= new PipelineProfile(false, TextWriter.Null, "fix");
        using var preparation = profile.Measure("fix.preparation");
        var prepared = new List<PreparedSourceFile>();
        var changed = new List<string>();
        string[] targets = [];
        string? current = null;
        var committing = false;
        FixApplicationResult result;
        try
        {
            var plan = BundleResponseProtocol.Read(response, snapshot);
            var documents = snapshot
                .Projects.SelectMany(project => project.Documents)
                .DistinctBy(document => document.FileIdentity)
                .ToDictionary(document => document.FileIdentity);
            var groups = plan
                .Edits.GroupBy(edit => edit.FileIdentity)
                .OrderBy(group => documents[group.Key].Path, StringComparer.Ordinal)
                .ToArray();
            targets = groups
                .Select(group => _fileSystem.Path.GetFullPath(documents[group.Key].Path))
                .ToArray();
            var states = _policy.Inspect(snapshot, cancellationToken);
            foreach (var group in groups)
            {
                current = _fileSystem.Path.GetFullPath(documents[group.Key].Path);
                var file = await PrepareContentAsync(
                    documents[group.Key],
                    group,
                    states,
                    cancellationToken
                );
                if (file is not null)
                {
                    prepared.Add(file);
                }
            }

            foreach (var file in prepared)
            {
                current = file.Path;
                await _replacer.PrepareAsync(file, cancellationToken);
            }

            preparation.Dispose();
            profile.Count("fix.prepared.files", prepared.Count);
            using var commit = profile.Measure("fix.commit");
            committing = true;
            foreach (var file in prepared)
            {
                current = file.Path;
                await _replacer.ReplaceAsync(snapshot, file, cancellationToken);
                changed.Add(file.Path);
            }

            result = new(FixApplicationOutcome.Completed, changed.ToArray(), null, [], null);
        }
        catch (Exception exception)
        {
            var outcome =
                exception is OperationCanceledException ? FixApplicationOutcome.Cancelled
                : committing ? FixApplicationOutcome.CommitFailed
                : FixApplicationOutcome.PreparationFailed;
            result = new(
                outcome,
                changed.ToArray(),
                current,
                targets.Except(changed).Where(path => path != current).ToArray(),
                exception.Message
            );
        }

        preparation.Dispose();
        profile.Count("fix.changed.files", changed.Count);
        return Cleanup(prepared, result);
    }

    private FixApplicationResult Cleanup(
        IEnumerable<PreparedSourceFile> prepared,
        FixApplicationResult result
    )
    {
        foreach (var file in prepared.Where(file => file.TemporaryCreated))
        {
            try
            {
                _fileSystem.File.Delete(file.TemporaryPath);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                result = result with
                {
                    Outcome =
                        result.Outcome == FixApplicationOutcome.Completed
                            ? FixApplicationOutcome.CommitFailed
                            : result.Outcome,
                    Error =
                        $"{result.Error} Temporary cleanup failed for '{file.TemporaryPath}': {exception.Message}".Trim(),
                };
            }
        }

        return result;
    }

    private async Task<PreparedSourceFile?> PrepareContentAsync(
        DocumentSnapshot document,
        IEnumerable<SourceEdit> edits,
        IReadOnlyDictionary<string, SourceFileState> states,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = states[document.FileIdentity];
        if (states.Values.Any(value => value.Identity is null) || !state.IsEditable)
        {
            throw new IOException(
                "Source has an unknown identity, ambiguous alias, or non-editable target."
            );
        }

        var original = await _fileSystem.File.ReadAllBytesAsync(state.Path, cancellationToken);
        if (!original.AsSpan().SequenceEqual(SourceIdentity.Encode(document)))
        {
            throw new IOException("Source bytes changed since analysis.");
        }

        var text = document.Text;
        foreach (var edit in edits.OrderByDescending(edit => edit.Start))
        {
            text = string.Concat(
                text.AsSpan(0, edit.Start),
                edit.Replacement,
                text.AsSpan(edit.Start + edit.Length)
            );
        }

        var replacement = SourceIdentity.Encode(document with { Text = text });
        if (original.AsSpan().SequenceEqual(replacement))
        {
            return null;
        }

        var temporaryPath = _fileSystem.Path.Combine(
            _fileSystem.Path.GetDirectoryName(state.Path)!,
            $".dp-{Guid.NewGuid():N}.tmp"
        );
        return new(document, state.Path, temporaryPath, state.Identity!, original, replacement);
    }
}
