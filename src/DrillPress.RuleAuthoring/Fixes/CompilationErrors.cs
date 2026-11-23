using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

// A compilation's errors, keeping apart the warnings reported as errors that a project suppressor may hide, such as
// EF Core's CS8618 on DbSet properties. Rules may run as native code, which cannot load the project's suppressors,
// so fix validation tolerates those errors as long as an edit adds none.
internal sealed class CompilationErrors
{
    private readonly Dictionary<SuppressibleError, List<Diagnostic>> _suppressible;

    private CompilationErrors(
        Diagnostic? blocking,
        Dictionary<SuppressibleError, List<Diagnostic>> suppressible
    )
    {
        Blocking = blocking;
        _suppressible = suppressible;
    }

    /// <summary>The first error that no suppressor of the project can hide, or null.</summary>
    internal Diagnostic? Blocking { get; }

    internal static CompilationErrors Read(AnalysisProject project, Compilation compilation)
    {
        var ids = project.Snapshot.SuppressibleDiagnosticIds;
        Diagnostic? blocking = null;
        var suppressible = new Dictionary<SuppressibleError, List<Diagnostic>>();
        foreach (var error in compilation.GetDiagnostics(project.CancellationToken))
        {
            if (error.Severity != DiagnosticSeverity.Error)
                continue;
            if (!error.IsWarningAsError || !ids.Contains(error.Id))
            {
                blocking = error;
                break;
            }

            var key = new SuppressibleError(
                error.Location.SourceTree?.FilePath ?? "",
                Declaration(compilation, error, project.CancellationToken),
                error.Id,
                error.GetMessage(CultureInfo.InvariantCulture)
            );
            if (!suppressible.TryGetValue(key, out var same))
                suppressible.Add(key, same = []);
            same.Add(error);
        }

        return new(blocking, suppressible);
    }

    /// <summary>The first error this edited compilation adds to the original: one no suppressor can hide, or a suppressible one beyond those the same declaration already reports with the same message.</summary>
    internal Diagnostic? AddedTo(CompilationErrors original) =>
        Blocking
        ?? _suppressible
            .Select(pair =>
                pair.Value.ElementAtOrDefault(
                    original._suppressible.GetValueOrDefault(pair.Key)?.Count ?? 0
                )
            )
            .FirstOrDefault(error => error is not null);

    // The innermost declared member or variable with its container, so that an edit cannot trade a suppressed
    // warning for one with the same message on another type's member or another method's local.
    private static string Declaration(
        Compilation compilation,
        Diagnostic error,
        CancellationToken cancellationToken
    )
    {
        if (error.Location.SourceTree is not { } tree)
            return "";
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot(cancellationToken)
                .FindNode(error.Location.SourceSpan, getInnermostNodeForTie: true)
                .AncestorsAndSelf()
                .Where(node => node is MemberDeclarationSyntax or VariableDeclaratorSyntax)
                .Select(node => model.GetDeclaredSymbol(node, cancellationToken))
                .OfType<ISymbol>()
                .Select(symbol =>
                    $"{symbol.ContainingSymbol?.ToDisplayString()} {symbol.ToDisplayString()}"
                )
                .FirstOrDefault()
            ?? "";
    }

    private sealed record SuppressibleError(
        string Path,
        string Declaration,
        string Id,
        string Message
    );
}
