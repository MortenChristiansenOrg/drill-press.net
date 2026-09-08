namespace DrillPress.Manifest;

/// <summary>OS metadata used to detect aliases without changing stable snapshot path identities.</summary>
/// <param name="Key">Device/volume and inode/file identifier, valid on the current machine.</param>
/// <param name="LinkCount">Number of hard links to the target.</param>
/// <param name="IsRegularFile">Whether the target is an ordinary file rather than a directory or special device.</param>
public sealed record PhysicalFileIdentity(string Key, uint LinkCount, bool IsRegularFile);

/// <summary>Current filesystem eligibility, separate from the source bytes captured for analysis.</summary>
/// <param name="Path">Absolute path inspected through the injected filesystem.</param>
/// <param name="Identity">Physical identity, or null when the OS could not establish it.</param>
/// <param name="IsEditable">Whether this path has an unambiguous, writable ordinary target.</param>
public sealed record SourceFileState(string Path, PhysicalFileIdentity? Identity, bool IsEditable);
