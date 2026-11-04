using DrillPress.Manifest;

namespace DrillPress.Cli;

/// <summary>Confirms after a fix that every moved file is compiled by exactly the projects that compiled it before.</summary>
internal static class MovedMembership
{
    /// <summary>Throws when the recaptured target compiles a moved file in other projects or frameworks, or still compiles its old path.</summary>
    internal static void Verify(
        CompilationSnapshot before,
        CompilationSnapshot after,
        IReadOnlyList<MovedFile> moves
    )
    {
        foreach (var move in moves)
        {
            if (
                !Contexts(before, move.From).SetEquals(Contexts(after, move.To))
                || !string.Equals(move.From, move.To, StringComparison.OrdinalIgnoreCase)
                    && Contexts(after, move.From).Count > 0
            )
                throw new InvalidDataException(
                    $"'{move.To}' is not compiled by the same projects as before the move."
                );
        }
    }

    private static HashSet<(string Project, string Framework)> Contexts(
        CompilationSnapshot snapshot,
        string path
    ) =>
        snapshot
            .Projects.Where(project =>
                project.Documents.Any(document =>
                    !document.IsGenerated
                    && string.Equals(
                        document.Path,
                        path,
                        OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal
                    )
                )
            )
            .Select(project => (project.ProjectPath, project.TargetFramework))
            .ToHashSet();
}
