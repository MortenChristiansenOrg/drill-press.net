namespace DrillPress.Manifest;

/// <summary>One accepted C# source file, keyed by the path it has, or had, in the current tree.</summary>
/// <param name="Path">The full current-tree path that corresponds to the accepted file.</param>
/// <param name="Content">The accepted file's bytes, decoded later with the current file's encoding.</param>
/// <param name="Removed">Whether the path is absent from the current tree, as after a deletion or a move.</param>
public sealed record AcceptedSourceFile(string Path, byte[] Content, bool Removed);
