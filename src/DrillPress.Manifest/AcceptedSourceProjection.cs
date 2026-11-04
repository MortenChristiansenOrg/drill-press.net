using System.Text;

namespace DrillPress.Manifest;

/// <summary>
/// Rebuilds the current compilation contexts with accepted source text. Project evaluation,
/// references and generated sources stay current, so only ordinary source membership differs.
/// </summary>
internal static class AcceptedSourceProjection
{
    private static readonly StringComparison _pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly StringComparer _paths = StringComparer.FromComparison(_pathComparison);

    internal static CompilationSnapshot Project(
        CompilationSnapshot current,
        AcceptedSources accepted
    )
    {
        var root = Directory(accepted.Root);
        var files = new Dictionary<string, AcceptedSourceFile>(_paths);
        foreach (var file in accepted.Files)
            files[Normalize(file.Path)] = file;
        var projectDirectories = current
            .Projects.Select(project => Directory(ParentOf(project.ProjectPath)))
            .Distinct(_paths)
            .ToArray();
        var removed = files
            .Values.Where(file => file.Removed)
            .ToLookup(file => OwningDirectory(file.Path, projectDirectories), _paths);
        return current with
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Projects = current
                .Projects.Select(project =>
                    project with
                    {
                        Documents = Documents(project, root, files, removed),
                    }
                )
                .ToArray(),
        };
    }

    private static DocumentSnapshot[] Documents(
        ProjectSnapshot project,
        string root,
        Dictionary<string, AcceptedSourceFile> files,
        ILookup<string?, AcceptedSourceFile> removed
    )
    {
        var documents = new List<DocumentSnapshot>();
        var paths = new HashSet<string>(_paths);
        foreach (var document in project.Documents)
        {
            var path = Normalize(document.Path);
            paths.Add(path);
            if (document.IsGenerated || !path.StartsWith(root, _pathComparison))
                documents.Add(document with { IsEditable = false, Fingerprint = "" });
            else if (files.TryGetValue(path, out var file))
                documents.Add(
                    document with
                    {
                        Text = Decode(file.Content, AcceptedEncoding(document)),
                        IsEditable = false,
                        Fingerprint = "",
                    }
                );
        }

        var directory = Directory(ParentOf(project.ProjectPath));
        documents.AddRange(
            removed[directory]
                .Where(file => !paths.Contains(Normalize(file.Path)))
                .OrderBy(file => file.Path, StringComparer.Ordinal)
                .Select(file => new DocumentSnapshot(
                    file.Path,
                    Decode(file.Content, new UTF8Encoding(false)),
                    false
                ))
        );
        return documents.ToArray();
    }

    // Removed files belong to the deepest project directory that contains them.
    private static string? OwningDirectory(string path, string[] projectDirectories) =>
        projectDirectories
            .Where(directory => Normalize(path).StartsWith(directory, _pathComparison))
            .MaxBy(directory => directory.Length);

    // A current byte-order mark says nothing about accepted bytes; without one, the compiler falls back to UTF-8.
    private static Encoding AcceptedEncoding(DocumentSnapshot document) =>
        document.HasByteOrderMark
            ? new UTF8Encoding(false)
            : Encoding.GetEncoding(document.EncodingName);

    private static string Decode(byte[] content, Encoding encoding)
    {
        using var reader = new StreamReader(
            new MemoryStream(content),
            encoding,
            detectEncodingFromByteOrderMarks: true
        );
        return reader.ReadToEnd();
    }

    private static string ParentOf(string path) =>
        Normalize(path) is var normalized && normalized.LastIndexOf('/') is var index and >= 0
            ? normalized[..index]
            : "";

    private static string Directory(string path) => Normalize(path).TrimEnd('/') + "/";

    private static string Normalize(string path) => path.Replace('\\', '/');
}
