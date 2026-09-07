using System.IO.Abstractions;
using DrillPress.BundleVerification;

namespace DrillPress.Benchmarks;

public sealed class ArtifactMeasurements(IFileSystem fileSystem)
{
    public ArtifactMeasurements() : this(new FileSystem())
    {
    }

    public ArtifactInventory Read(BundleMode mode, string directory)
    {
        var files = fileSystem.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => fileSystem.Path.GetExtension(path).ToLowerInvariant() is not (".pdb" or ".dbg" or ".xml"))
            .Select(path => new ArtifactFile(
                fileSystem.Path.GetRelativePath(directory, path).Replace('\\', '/'), fileSystem.FileInfo.New(path).Length))
            .OrderBy(file => file.Path)
            .ToArray();
        return new ArtifactInventory(mode, files.Sum(file => file.Bytes), files);
    }
}
