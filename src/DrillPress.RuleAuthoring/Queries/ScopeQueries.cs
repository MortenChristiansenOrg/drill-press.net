using DrillPress.Configuration;
using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Physical document and semantic namespace scopes for reportable source candidates.</summary>
public static class ScopeQueries
{
    /// <summary>Selects candidates in the named evaluated project, using an ordinal name comparison.</summary>
    public static CodeQuery<T> InProject<T>(this CodeQuery<T> query, string name)
        where T : ICodeElement
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return query.Where(element => element.IsDeclaredInProject(name));
    }

    /// <summary>Tests the candidate's evaluated project name.</summary>
    public static bool IsDeclaredInProject(this ICodeElement element, string name) =>
        element.Source?.Project.Name == name;

    /// <summary>Matches filename globs, excluding the containing directories.</summary>
    public static CodeQuery<T> InFilesNamed<T>(this CodeQuery<T> query, string pattern)
        where T : ICodeElement
    {
        var match = new PathPattern(pattern);
        return query.Where(element =>
            element.Source is { } source && match.Matches(new CodeFile(source).Name)
        );
    }

    /// <summary>Matches whole physical folder segments at any depth relative to the project. Loose sources use sourceRoot, or the invocation directory captured when analysis began. Linked-item logical folders are not consulted.</summary>
    public static CodeQuery<T> InFolder<T>(
        this CodeQuery<T> query,
        string folder,
        string? sourceRoot = null
    )
        where T : ICodeElement
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var match = new PathPattern("**/" + folder.Replace('\\', '/').Trim('/') + "/**");
        return query.Where(element =>
            element.Source is { } source && match.Matches(RelativePath(source, sourceRoot))
        );
    }

    /// <summary>Matches namespace segments; * matches one segment and a trailing ** includes the root and all descendants.</summary>
    public static CodeQuery<T> InNamespace<T>(this CodeQuery<T> query, string pattern)
        where T : ICodeElement
    {
        var match = NamespacePattern(pattern);
        return query.Where(element => MatchesNamespace(element, match));
    }

    /// <summary>Tests the candidate's semantic namespace with the same segment rules as InNamespace.</summary>
    public static bool IsInNamespace(this ICodeElement element, string pattern) =>
        MatchesNamespace(element, NamespacePattern(pattern));

    private static PathPattern NamespacePattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return new(pattern.Replace('.', '/') + "/");
    }

    private static bool MatchesNamespace(ICodeElement element, PathPattern pattern)
    {
        if (element.Source is not { } source)
            return false;
        var symbol = source.Model.GetEnclosingSymbol(
            element.Location.Start,
            source.Project.CancellationToken
        );
        var space = symbol as INamespaceSymbol ?? symbol?.ContainingNamespace;
        return space is not null
            && pattern.Matches(
                (space.IsGlobalNamespace ? "" : space.ToDisplayString().Replace('.', '/')) + "/"
            );
    }

    private static string RelativePath(AnalysisSource source, string? sourceRoot)
    {
        var project = source.Project;
        var path = project.FileSystem.Path;
        var root =
            (string.IsNullOrEmpty(project.ProjectPath) || project.Snapshot.ContextId == "loose")
                ? sourceRoot ?? project.InvocationDirectory
                : path.GetDirectoryName(
                    path.GetFullPath(project.ProjectPath, project.InvocationDirectory)
                )!;
        var relative = path.GetRelativePath(
                path.GetFullPath(root, project.InvocationDirectory),
                path.GetFullPath(source.Document.Path, project.InvocationDirectory)
            )
            .Replace('\\', '/');
        return
            relative == ".."
            || relative.StartsWith("../", StringComparison.Ordinal)
            || path.IsPathRooted(relative)
            ? ""
            : relative;
    }
}
