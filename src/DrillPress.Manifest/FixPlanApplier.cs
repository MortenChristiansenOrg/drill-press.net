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
    /// Validates the exact bundle response, prepares every output, then replaces files once and renames moved files after their content.
    /// Concurrent writers must be stopped: verification and replacement cannot form a cross-process transaction.
    /// Earlier successful replacements are retained on failure; unused temporary files are removed.
    /// Optional complexity selection runs after complete validation and withholds batches shared with excluded findings.
    /// </summary>
    public async Task<FixApplicationResult> ApplyAsync(
        CompilationSnapshot snapshot,
        string response,
        CancellationToken cancellationToken = default,
        PipelineProfile? profile = null,
        IReadOnlySet<RuleFixComplexity?>? fixComplexities = null
    )
    {
        profile ??= new PipelineProfile(false, TextWriter.Null, "fix");
        using var preparation = profile.Measure("fix.preparation");
        var prepared = new List<PreparedSourceFile>();
        var changed = new List<string>();
        var moved = new List<MovedFile>();
        string[] targets = [];
        string? current = null;
        var committing = false;
        FixApplicationResult result;
        try
        {
            var plan = BundleResponseProtocol.Read(response, snapshot, fixComplexities);
            var documents = snapshot
                .Projects.SelectMany(project => project.Documents)
                .DistinctBy(document => document.FileIdentity)
                .ToDictionary(document => document.FileIdentity);
            var edits = plan.Edits.ToLookup(edit => edit.FileIdentity);
            var moves = plan.Moves.ToDictionary(
                move => move.FileIdentity,
                move => move.Destination
            );
            var files = plan
                .Edits.Select(edit => edit.FileIdentity)
                .Concat(moves.Keys)
                .Distinct()
                .OrderBy(identity => documents[identity].Path, StringComparer.Ordinal)
                .ToArray();
            targets = files
                .Select(identity => _fileSystem.Path.GetFullPath(documents[identity].Path))
                .ToArray();
            var states = _policy.Inspect(snapshot, cancellationToken);
            foreach (var identity in files)
            {
                current = _fileSystem.Path.GetFullPath(documents[identity].Path);
                var file = await PrepareContentAsync(
                    documents[identity],
                    edits[identity],
                    states,
                    cancellationToken
                );
                if (moves.TryGetValue(identity, out var destination))
                {
                    file = file with
                    {
                        Destination = _fileSystem.Path.GetFullPath(destination),
                        ProjectDirectories = snapshot
                            .Projects.Where(project =>
                                project.Documents.Any(document => document.FileIdentity == identity)
                            )
                            .Select(project =>
                                _fileSystem.Path.GetFullPath(project.CompileGlob!.Directory)
                            )
                            .Distinct()
                            .ToArray(),
                    };
                }

                if (file.ChangesContent || file.Destination is not null)
                {
                    prepared.Add(file);
                }
            }

            foreach (var file in prepared)
            {
                current = file.Path;
                if (file.ChangesContent)
                    await _replacer.PrepareAsync(file, cancellationToken);
                if (file.Destination is not null)
                    _replacer.CheckDestination(file);
            }

            preparation.Dispose();
            profile.Count("fix.prepared.files", prepared.Count);
            using var commit = profile.Measure("fix.commit");
            committing = true;
            foreach (var file in prepared)
            {
                current = file.Path;
                if (file.ChangesContent)
                    await _replacer.ReplaceAsync(snapshot, file, cancellationToken);
                if (file.Destination is { } destination)
                {
                    await _replacer.MoveAsync(snapshot, file, cancellationToken);
                    moved.Add(new(file.Path, destination));
                    snapshot = WithPath(snapshot, file.Document.FileIdentity, destination);
                }
                changed.Add(file.Destination ?? file.Path);
            }

            result = new(FixApplicationOutcome.Completed, changed.ToArray(), null, [], null)
            {
                Moved = moved.ToArray(),
            };
        }
        catch (Exception exception)
        {
            var outcome =
                exception is OperationCanceledException ? FixApplicationOutcome.Cancelled
                : committing ? FixApplicationOutcome.CommitFailed
                : FixApplicationOutcome.PreparationFailed;
            var completed = moved.Select(move => move.From).Concat(changed).ToHashSet();
            result = new(
                outcome,
                changed.ToArray(),
                current,
                targets.Where(path => !completed.Contains(path) && path != current).ToArray(),
                exception.Message
            )
            {
                Moved = moved.ToArray(),
            };
        }

        preparation.Dispose();
        profile.Count("fix.changed.files", changed.Count);
        return Cleanup(prepared, result);
    }

    // Later identity checks inspect a moved file at its new path, so its old path is not an unknown alias.
    private static CompilationSnapshot WithPath(
        CompilationSnapshot snapshot,
        string identity,
        string path
    ) =>
        snapshot with
        {
            Projects = snapshot
                .Projects.Select(project =>
                    project with
                    {
                        Documents = project
                            .Documents.Select(document =>
                                document.FileIdentity == identity
                                    ? document with
                                    {
                                        Path = path,
                                    }
                                    : document
                            )
                            .ToArray(),
                    }
                )
                .ToArray(),
        };

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

    private async Task<PreparedSourceFile> PrepareContentAsync(
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
        var temporaryPath = _fileSystem.Path.Combine(
            _fileSystem.Path.GetDirectoryName(state.Path)!,
            $".dp-{Guid.NewGuid():N}.tmp"
        );
        return new(document, state.Path, temporaryPath, state.Identity!, original, replacement);
    }
}
