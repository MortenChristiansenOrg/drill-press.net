using System.IO.Abstractions;

namespace DrillPress.Manifest;

/// <summary>Establishes edit eligibility across all loaded physical source aliases.</summary>
public sealed class SourceFilePolicy
{
    private readonly IFileSystem _fileSystem;
    private readonly FileIdentityProbe _probe;

    /// <summary>Inspects local files with real OS identity metadata.</summary>
    public SourceFilePolicy()
        : this(new FileSystem(), new FileIdentityProbe()) { }

    internal SourceFilePolicy(IFileSystem fileSystem, FileIdentityProbe probe)
    {
        _fileSystem = fileSystem;
        _probe = probe;
    }

    /// <summary>Inspects ordinary sources and generated paths present on disk, withholding all loaded aliases.</summary>
    public IReadOnlyDictionary<string, SourceFileState> Inspect(
        CompilationSnapshot snapshot,
        CancellationToken cancellationToken = default
    )
    {
        var states = new Dictionary<string, SourceFileState>();
        foreach (
            var document in snapshot
                .Projects.SelectMany(project => project.Documents)
                .Where(document => !document.IsGenerated || _fileSystem.File.Exists(document.Path))
                .DistinctBy(document => document.FileIdentity)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = Inspect(document.Path);
            states.Add(
                document.FileIdentity,
                state with
                {
                    IsEditable = state.IsEditable && !document.IsGenerated,
                }
            );
        }

        var aliases = states
            .Where(pair => pair.Value.Identity is not null)
            .GroupBy(pair => pair.Value.Identity!.Key)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(pair => pair.Key))
            .ToArray();
        foreach (var alias in aliases)
        {
            states[alias] = states[alias] with { IsEditable = false };
        }

        return states;
    }

    /// <summary>Inspects a path before a replacement; callers must also check aliases elsewhere in the loaded graph.</summary>
    public SourceFileState Inspect(string path)
    {
        path = _fileSystem.Path.GetFullPath(path);
        try
        {
            var identity = _probe.Read(path);
            var editable =
                identity is { LinkCount: 1, IsRegularFile: true }
                && _fileSystem.File.Exists(path)
                && (_fileSystem.File.GetAttributes(path) & FileAttributes.ReadOnly) == 0
                && !HasSymbolicAlias(path);
            return new(path, identity, editable);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new(path, null, false);
        }
    }

    /// <summary>Restricts captured edit flags without changing source text, membership IDs, or stable path identities.</summary>
    public CompilationSnapshot Restrict(
        CompilationSnapshot snapshot,
        CancellationToken cancellationToken = default
    )
    {
        var states = Inspect(snapshot, cancellationToken);
        var known = states.Values.All(state => state.Identity is not null);
        return snapshot with
        {
            Projects = snapshot
                .Projects.Select(project =>
                    project with
                    {
                        Documents = project
                            .Documents.Select(document =>
                                document with
                                {
                                    IsEditable =
                                        document.IsEditable
                                        && known
                                        && states.TryGetValue(document.FileIdentity, out var state)
                                        && state.IsEditable,
                                }
                            )
                            .ToArray(),
                    }
                )
                .ToArray(),
        };
    }

    private bool HasSymbolicAlias(string path)
    {
        if ((_fileSystem.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }

        var directory = _fileSystem.DirectoryInfo.New(_fileSystem.Path.GetDirectoryName(path)!);
        while (directory is not null)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }
}
