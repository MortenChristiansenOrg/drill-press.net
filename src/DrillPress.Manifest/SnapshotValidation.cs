namespace DrillPress.Manifest;

/// <summary>Checks source identities and graph membership before evaluation or response validation.</summary>
public static class SnapshotValidation
{
    /// <summary>Rejects incomplete, incompatible, and internally inconsistent compiler inputs.</summary>
    public static void Validate(CompilationSnapshot snapshot)
    {
        ValidateEnvelope(snapshot.FileIdentifier, snapshot.FormatVersion);
        Require(
            !string.IsNullOrWhiteSpace(snapshot.RequestId),
            "Snapshot request identity is missing."
        );
        var contexts = new HashSet<string>();
        var documents = new HashSet<string>();
        var files = new Dictionary<string, DocumentSnapshot>();
        var paths = new Dictionary<string, string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : EqualityComparer<string>.Default
        );
        foreach (var project in snapshot.Projects)
        {
            Require(
                !string.IsNullOrWhiteSpace(project.ContextId) && contexts.Add(project.ContextId),
                "Duplicate or missing context identity."
            );
            foreach (var document in project.Documents)
            {
                Require(
                    !string.IsNullOrWhiteSpace(document.DocumentId)
                        && documents.Add(document.DocumentId),
                    "Duplicate or missing document identity."
                );
                Require(
                    !string.IsNullOrWhiteSpace(document.FileIdentity)
                        && !string.IsNullOrWhiteSpace(document.Path),
                    "Missing file identity or path."
                );
                Require(
                    !paths.TryGetValue(document.Path, out var identity)
                        || identity == document.FileIdentity,
                    "Ambiguous source path identity."
                );
                paths[document.Path] = document.FileIdentity;
                SourceIdentity.Validate(document);
                if (files.TryGetValue(document.FileIdentity, out var previous))
                {
                    Require(
                        previous.Text == document.Text
                            && previous.Fingerprint == document.Fingerprint
                            && previous.IsEditable == document.IsEditable
                            && previous.IsGenerated == document.IsGenerated
                            && previous.Path == document.Path
                            && previous.EncodingName == document.EncodingName
                            && previous.HasByteOrderMark == document.HasByteOrderMark,
                        "Inconsistent linked source identity."
                    );
                }
                else
                {
                    files.Add(document.FileIdentity, document);
                }
            }
        }

        Require(
            snapshot.Projects.All(project => project.ReferencedContextIds.All(contexts.Contains)),
            "Unknown project reference context."
        );
        foreach (var project in snapshot.Projects)
        {
            Require(
                project.CompilationReferences.All(reference =>
                    contexts.Contains(reference.ContextId)
                ),
                "Unknown source compilation reference."
            );
            Require(
                project.CompilationReferences.Length == 0
                    || project
                        .CompilationReferences.Select(reference => reference.ContextId)
                        .ToHashSet()
                        .SetEquals(project.ReferencedContextIds),
                "Compilation references disagree with the context graph."
            );
            foreach (var reference in project.ExternalReferences)
            {
                Require(
                    !string.IsNullOrWhiteSpace(reference.Path)
                        && reference.Fingerprint.Length == 64
                        && reference.Fingerprint.All(character => char.IsAsciiHexDigit(character)),
                    "Invalid external metadata identity."
                );
                Require(reference.Kind is 0 or 1, "Invalid metadata reference kind.");
            }
        }
    }

    internal static void ValidateEnvelope(string identifier, int version)
    {
        Require(
            identifier == CompilationSnapshot.ExpectedFileIdentifier,
            "The input is not a Drill Press compilation snapshot."
        );
        Require(
            version == CompilationSnapshot.CurrentFormatVersion,
            $"Compilation snapshot format {version} is not supported; expected {CompilationSnapshot.CurrentFormatVersion}. Use matching Drill Press components."
        );
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
