using DrillPress;
using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Project, file, folder and namespace scopes shared by every reportable source element.</summary>
public static class ScopeQueries
{
    /// <summary>Selects elements in projects that BuildHost classifies as test projects, without project naming assumptions.</summary>
    public static CodeQuery<T> InTestProjects<T>(this CodeQuery<T> query)
        where T : ICodeElement => query.Where(element => element.Source.Project.IsTestProject);

    /// <summary>Selects elements in projects that BuildHost does not classify as test projects.</summary>
    public static CodeQuery<T> InNonTestProjects<T>(this CodeQuery<T> query)
        where T : ICodeElement => query.Where(element => !element.Source.Project.IsTestProject);

    /// <summary>Selects elements whose evaluated compilation uniquely contains the configured type identity, including through transitive references.</summary>
    public static CodeQuery<T> InProjectsWithType<T>(this CodeQuery<T> query, CodeType type)
        where T : ICodeElement => query.Where(element => element.Source.Project.HasType(type));

    /// <summary>Selects elements in evaluated projects whose name matches a case-sensitive glob, such as <c>Contoso.Api</c> or <c>Contoso.*</c>.</summary>
    public static CodeQuery<T> InProject<T>(this CodeQuery<T> query, string pattern)
        where T : ICodeElement
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var match = new PathPattern(pattern);
        return query.Where(element => match.Matches(element.Source.Project.Name));
    }

    /// <summary>Tests the element's evaluated project name against a case-sensitive glob.</summary>
    public static bool IsInProject(this ICodeElement element, string pattern) =>
        new PathPattern(pattern).Matches(element.Source.Project.Name);

    /// <summary>Matches filename globs, excluding the containing directories.</summary>
    public static CodeQuery<T> InFilesNamed<T>(this CodeQuery<T> query, string pattern)
        where T : ICodeElement
    {
        var match = new PathPattern(pattern);
        return query.Where(element => match.Matches(new CodeFile(element.Source).Name));
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
        return query.Where(element => match.Matches(RelativePath(element.Source, sourceRoot)));
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
        var source = element.Source;
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
