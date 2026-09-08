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
            .Select(path => new ArtifactFingerprint(_fileSystem.Path.GetRelativePath(directory, path).Replace(_fileSystem.Path.DirectorySeparatorChar, '/'),
                _fileSystem.FileInfo.New(path).Length, Convert.ToHexString(SHA256.HashData(_fileSystem.File.ReadAllBytes(path)))))
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        return new(entryPoint, files);
    }
}
