using System.IO.Abstractions;
using System.Security.Cryptography;

namespace DrillPress.Benchmarks;

public sealed class PublishedBundleIdentity(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    public BundleIdentity Read(string entryPoint)
    {
        entryPoint = _fileSystem.Path.GetFullPath(entryPoint);
        var directory = _fileSystem.Path.GetDirectoryName(entryPoint)!;
        var files = _fileSystem.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Fingerprint(directory, path))
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        return new(entryPoint, files);
    }

    private ArtifactFingerprint Fingerprint(string directory, string path)
    {
        using var content = _fileSystem.File.OpenRead(path);
        return new(_fileSystem.Path.GetRelativePath(directory, path).Replace(_fileSystem.Path.DirectorySeparatorChar, '/'),
            content.Length, Convert.ToHexString(SHA256.HashData(content)));
    }
}
