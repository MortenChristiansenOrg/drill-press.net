using DrillPress.Manifest;

namespace DrillPress;

/// <summary>A selected source file ready to be renamed or moved within its project.</summary>
public sealed class FileFix
{
    private readonly CodeFile _file;

    internal FileFix(CodeFile file) => _file = file;

    /// <summary>Renames the file within its folder, such as <c>RenameTo("OrderService.cs")</c>. End with <see cref="FileMove.Propose"/> or <see cref="FileMove.SafeWhen"/>.</summary>
    /// <param name="fileName">The new file name ending in <c>.cs</c>, without a folder.</param>
    public FileMove RenameTo(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException(
                "Use a file name without folders; MoveTo changes folders.",
                nameof(fileName)
            );
        return new(
            _file,
            SourcePaths.Join(SourcePaths.Directory(_file.Source.Document.Path), fileName)
        );
    }

    /// <summary>Moves the file to a path below its project directory, written like <see cref="CodeFile.RelativePath"/>, such as <c>MoveTo("Orders/OrderServiceTests.cs")</c>. End with <see cref="FileMove.Propose"/> or <see cref="FileMove.SafeWhen"/>.</summary>
    /// <param name="relativePath">The new path below the project directory, with forward slashes and without <c>.</c> or <c>..</c> segments.</param>
    public FileMove MoveTo(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (
            relativePath.Contains('\\')
            || relativePath.StartsWith('/')
            || !SourcePaths.IsNormalized(relativePath)
            || SourcePaths.IsBelow(relativePath, "") is false
        )
            throw new ArgumentException(
                "Use a relative path with forward slashes, without '.' or '..' segments.",
                nameof(relativePath)
            );
        return new(
            _file,
            _file.Source.Project.Snapshot.CompileGlob is { } glob
                ? SourcePaths.Join(glob.Directory, relativePath)
                : null
        );
    }
}
