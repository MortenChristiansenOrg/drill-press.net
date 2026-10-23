using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Project, file, folder, namespace, type, method and loop scopes shared by every reportable source element.</summary>
public static class ScopeQueries
{
    /// <summary>Selects elements in projects that BuildHost classifies as test projects, without project naming assumptions.</summary>
    public static CodeQuery<T> InTestProjects<T>(this CodeQuery<T> query)
        where T : ICodeElement => query.InSources(source => source.Project.IsTestProject);

    /// <summary>Selects elements in projects that BuildHost does not classify as test projects.</summary>
    public static CodeQuery<T> InNonTestProjects<T>(this CodeQuery<T> query)
        where T : ICodeElement => query.InSources(source => !source.Project.IsTestProject);

    /// <summary>Selects elements whose evaluated compilation uniquely contains the configured type identity, including through transitive references.</summary>
    public static CodeQuery<T> InProjectsWithType<T>(this CodeQuery<T> query, CodeType type)
        where T : ICodeElement => query.InSources(source => source.Project.HasType(type));

    /// <summary>Selects elements in evaluated projects whose name matches a case-sensitive glob, such as <c>Contoso.Api</c> or <c>Contoso.*</c>.</summary>
    public static CodeQuery<T> InProject<T>(this CodeQuery<T> query, string pattern)
        where T : ICodeElement
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var match = new PathPattern(pattern);
        return query.InSources(source => match.Matches(source.Project.Name));
    }

    /// <summary>Selects projects that BuildHost classifies as test projects.</summary>
    public static CodeQuery<AnalysisProject> InTestProjects(
        this CodeQuery<AnalysisProject> projects
    ) => projects.Where(project => project.IsTestProject);

    /// <summary>Selects projects that BuildHost does not classify as test projects.</summary>
    public static CodeQuery<AnalysisProject> InNonTestProjects(
        this CodeQuery<AnalysisProject> projects
    ) => projects.Where(project => !project.IsTestProject);

    /// <summary>Selects projects whose compilation uniquely contains the configured type identity, including through transitive references.</summary>
    public static CodeQuery<AnalysisProject> InProjectsWithType(
        this CodeQuery<AnalysisProject> projects,
        CodeType type
    ) => projects.Where(project => project.HasType(type));

    /// <summary>Selects projects whose name matches a case-sensitive glob, such as <c>Contoso.*</c>.</summary>
    public static CodeQuery<AnalysisProject> InProject(
        this CodeQuery<AnalysisProject> projects,
        string pattern
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var match = new PathPattern(pattern);
        return projects.Where(project => match.Matches(project.Name));
    }

    /// <summary>Tests the element's evaluated project name against a case-sensitive glob.</summary>
    public static bool IsInProject(this ICodeElement element, string pattern) =>
        new PathPattern(pattern).Matches(element.Source.Project.Name);

    /// <summary>Matches filename globs, excluding the containing directories.</summary>
    public static CodeQuery<T> InFilesNamed<T>(this CodeQuery<T> query, string pattern)
        where T : ICodeElement
    {
        var match = new PathPattern(pattern);
        return query.InSources(source => match.Matches(new CodeFile(source).Name));
    }

    /// <summary>Matches whole physical folder segments at any depth relative to the project. Loose sources use sourceRoot, or the invocation directory captured when analysis began. Linked-item logical folders are not consulted.</summary>
    public static CodeQuery<T> InFolder<T>(
        this CodeQuery<T> query,
        string folder,
        string? sourceRoot = null
    )
        where T : ICodeElement
    {
        var match = FolderPattern(folder);
        return query.InSources(source => match.Matches(RelativePath(source, sourceRoot) ?? ""));
    }

    /// <summary>Tests whole physical folder segments with the same rules as InFolder.</summary>
    public static bool IsInFolder(
        this ICodeElement element,
        string folder,
        string? sourceRoot = null
    ) => FolderPattern(folder).Matches(RelativePath(element.Source, sourceRoot) ?? "");

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

    /// <summary>Tests the type whose code contains the element, and the types containing that one, by semantic identity. Code in lambdas and local functions belongs to the type that declares them.</summary>
    public static bool IsInType(this ICodeElement element, CodeType type)
    {
        for (var owner = EnclosingType(element); owner is not null; owner = owner.ContainingType)
            if (type.Matches(owner))
                return true;
        return false;
    }

    /// <summary>The method, constructor, accessor or operator whose body contains the element, walking out of lambdas and local functions; null in field initializers and other code outside a method.</summary>
    public static IMethodSymbol? ContainingMethod(this ICodeElement element)
    {
        var symbol = EnclosingSymbol(element);
        while (
            symbol
                is IMethodSymbol
                {
                    MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction
                }
        )
            symbol = symbol.ContainingSymbol;
        return symbol as IMethodSymbol;
    }

    /// <summary>The nearest for, foreach, while or do statement around the element, without leaving the lambda, local function or member that contains it.</summary>
    /// <remarks>Reads written syntax: any part of the loop counts, including a foreach collection, and a loop the element only reaches through a call does not.</remarks>
    public static CodeNode<StatementSyntax>? EnclosingLoop(this ICodeElement element)
    {
        foreach (var ancestor in ElementSyntax.Of(element).Ancestors())
        {
            if (
                ancestor
                is AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or MemberDeclarationSyntax
            )
                return null;
            if (
                ancestor
                is WhileStatementSyntax
                    or DoStatementSyntax
                    or ForStatementSyntax
                    or CommonForEachStatementSyntax
            )
                return new(element.Source, (StatementSyntax)ancestor);
        }
        return null;
    }

    /// <summary>Whether a for, foreach, while or do statement surrounds the element in its own function, as <see cref="EnclosingLoop"/> finds it.</summary>
    public static bool IsInLoop(this ICodeElement element) => element.EnclosingLoop() is not null;

    private static ISymbol? EnclosingSymbol(ICodeElement element) =>
        element.Source.Model.GetEnclosingSymbol(
            ElementSyntax.Of(element).SpanStart,
            element.Source.Project.CancellationToken
        );

    private static INamedTypeSymbol? EnclosingType(ICodeElement element) =>
        EnclosingSymbol(element) switch
        {
            INamedTypeSymbol type => type,
            { } symbol => symbol.ContainingType,
            null => null,
        };

    // A place filter decides by document alone, so query roots can skip other documents before binding them.
    private static CodeQuery<T> InSources<T>(
        this CodeQuery<T> query,
        Func<AnalysisSource, bool> accepts
    )
        where T : ICodeElement =>
        query.Where(
            new RuleCondition<T>(element => accepts(element.Source)) { SourceFilter = accepts }
        );

    private static PathPattern FolderPattern(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return new("**/" + folder.Replace('\\', '/').Trim('/') + "/**");
    }

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

    // The path below the project directory, or below sourceRoot or the invocation directory for loose sources; null outside it.
    internal static string? RelativePath(AnalysisSource source, string? sourceRoot)
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
            ? null
            : relative;
    }
}
