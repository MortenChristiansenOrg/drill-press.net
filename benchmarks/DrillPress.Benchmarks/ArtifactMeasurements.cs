using DrillPress.BundleVerification;

namespace DrillPress.Benchmarks;

public static class ArtifactMeasurements
{
    public static ArtifactInventory Read(BundleMode mode, string directory)
    {
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is not (".pdb" or ".dbg" or ".xml"))
            .Select(path => new ArtifactFile(
                Path.GetRelativePath(directory, path).Replace('\\', '/'), new FileInfo(path).Length))
            .OrderBy(file => file.Path)
            .ToArray();
        return new ArtifactInventory(mode, files.Sum(file => file.Bytes), files);
    }
}
