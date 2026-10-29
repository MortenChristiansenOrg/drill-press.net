using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Security.Cryptography;

namespace DrillPress.BuildHost;

// Scoped to one validation or capture, so equal paths share I/O without trusting timestamps.
internal sealed class InputFileFingerprints(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly ConcurrentDictionary<string, Lazy<string>> _files = new();

    internal string Read(string path) =>
        _files
            .GetOrAdd(
                path,
                path =>
                    new(() =>
                    {
                        if (!_fileSystem.File.Exists(path))
                            return "missing";
                        using var stream = _fileSystem.File.OpenRead(path);
                        return Convert.ToHexString(SHA256.HashData(stream));
                    })
            )
            .Value;
}
