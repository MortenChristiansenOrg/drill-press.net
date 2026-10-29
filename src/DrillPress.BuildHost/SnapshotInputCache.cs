using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using DrillPress.Manifest;

namespace DrillPress.BuildHost;

/// <summary>Reuses compiler capture only while its source, build inputs and directory inventories match.</summary>
internal sealed class SnapshotInputCache(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private SnapshotExport? _export;
    private string? _key;
    private string? _fingerprint;

    internal SnapshotExport? Find(string key, CancellationToken cancellationToken)
    {
        if (
            _key != key
            || _export is null
            || _fingerprint is null
            || _fingerprint != Fingerprint(_export, null, cancellationToken)
        )
            return null;
        var snapshot = _export.Snapshot with { RequestId = Guid.NewGuid().ToString("N") };
        return _export with { Snapshot = snapshot };
    }

    /// <summary>Keeps a capture for reuse unless an input changed after the capture started.</summary>
    /// <remarks>A file edited during capture would otherwise pair its old captured text with its new fingerprint.
    /// Timestamps only decide against caching; reuse still requires matching content.</remarks>
    internal void Store(
        string key,
        SnapshotExport export,
        DateTime captureStarted,
        CancellationToken cancellationToken
    )
    {
        var fingerprint = Fingerprint(export, captureStarted, cancellationToken);
        _key = key;
        _export = export;
        _fingerprint = fingerprint;
    }

    private string? Fingerprint(
        SnapshotExport export,
        DateTime? changedSince,
        CancellationToken cancellationToken
    )
    {
        var paths = export
            .InputPaths.Concat(
                export.Snapshot.Projects.SelectMany(project =>
                    project.ExternalReferences.Select(reference => reference.Path)
                )
            )
            .Concat(
                export.Snapshot.Projects.SelectMany(project =>
                    project
                        .Documents.Where(document => !document.IsGenerated)
                        .Select(document => document.Path)
                )
            )
            .Distinct()
            .Order(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var inputs = new InputFileFingerprints(_fileSystem);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ChangedSince(path, changedSince))
                return null;
            Append(hash, path);
            Append(hash, inputs.Read(path));
        }
        var visited = new HashSet<string>();
        foreach (
            var directory in export
                .Snapshot.Projects.Select(project =>
                    _fileSystem.Path.GetDirectoryName(project.ProjectPath)!
                )
                .Concat(export.InputDirectories)
                .Distinct()
                .Order(StringComparer.Ordinal)
        )
            if (!Inventory(directory, hash, visited, changedSince, cancellationToken))
                return null;
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private bool Inventory(
        string directory,
        IncrementalHash hash,
        HashSet<string> visited,
        DateTime? changedSince,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!visited.Add(directory))
            return true;
        Append(hash, directory);
        if (!_fileSystem.Directory.Exists(directory))
            return true;
        if (
            _fileSystem.DirectoryInfo.New(directory).LinkTarget is not null
            || ChangedSince(directory, changedSince)
        )
            return false;
        foreach (
            var entry in _fileSystem
                .Directory.EnumerateFileSystemEntries(directory)
                .Order(StringComparer.Ordinal)
        )
        {
            Append(hash, entry);
            if (
                _fileSystem.Directory.Exists(entry)
                && !Inventory(entry, hash, visited, changedSince, cancellationToken)
            )
                return false;
        }
        return true;
    }

    private bool ChangedSince(string path, DateTime? time) =>
        time is { } since
        && (
            _fileSystem.File.Exists(path) ? _fileSystem.File.GetLastWriteTimeUtc(path)
            : _fileSystem.Directory.Exists(path) ? _fileSystem.Directory.GetLastWriteTimeUtc(path)
            : DateTime.MinValue
        ) >= since;

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }
}
