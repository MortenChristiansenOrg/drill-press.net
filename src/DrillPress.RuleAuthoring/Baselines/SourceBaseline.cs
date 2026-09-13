using DrillPress.Queries;
using Microsoft.CodeAnalysis;

namespace DrillPress.Baselines;

/// <summary>An immutable accepted .NET source state supplied by the consumer. No Git dependency; compare analyses with stable project paths and evaluation properties.</summary>
public sealed class SourceBaseline
{
    private readonly Dictionary<(string Project, string File), string> _files = [];
    private readonly Dictionary<
        (string Project, string Symbol),
        (Accessibility Accessibility, string Text)
    > _symbols = [];

    /// <summary>Captures ordinary source text and declared symbol accessibility from an accepted analysis.</summary>
    public SourceBaseline(AnalysisSolution accepted)
    {
        foreach (var project in accepted.Projects)
        {
            var key = ProjectKey(project);
            foreach (var source in project.Sources.Where(source => !source.Document.IsGenerated))
            {
                _files[(key, Normalize(source.Document.Path))] = source.Document.Text;
                foreach (
                    var syntax in source.Tree.GetRoot(accepted.CancellationToken).DescendantNodes()
                )
                {
                    accepted.CancellationToken.ThrowIfCancellationRequested();
                    var symbol = source.Model.GetDeclaredSymbol(syntax, accepted.CancellationToken);
                    if (symbol?.GetDocumentationCommentId() is { } id)
                    {
                        _symbols[(key, id)] = (
                            symbol.DeclaredAccessibility,
                            DeclarationText(symbol, accepted.CancellationToken)
                        );
                    }
                }
            }
        }
    }

    /// <summary>Compares source text in the same project/framework/property context. A rename is an added membership.</summary>
    public SourceChange ChangeOf(CodeFile file) =>
        !_files.TryGetValue(
            (ProjectKey(file.Source.Project), Normalize(file.Source.Document.Path)),
            out var text
        )
            ? SourceChange.Added
        : text == file.Source.Document.Text ? SourceChange.Unchanged
        : SourceChange.Modified;

    /// <summary>Looks up a declared symbol's previous accessibility. Null means it was not captured under the same documentation identity and project context.</summary>
    public Accessibility? PreviousAccessibility(AnalysisProject project, ISymbol symbol) =>
        symbol.GetDocumentationCommentId() is { } id
        && _symbols.TryGetValue((ProjectKey(project), id), out var previous)
            ? previous.Accessibility
            : null;

    /// <summary>Compares a declared symbol's source, including partial declarations. Renamed or changed-signature symbols count as additions; trivia within declarations counts as a change.</summary>
    public SourceChange ChangeOf(AnalysisProject project, ISymbol symbol) =>
        symbol.GetDocumentationCommentId() is not { } id
        || !_symbols.TryGetValue((ProjectKey(project), id), out var previous)
            ? SourceChange.Added
        : previous.Text == DeclarationText(symbol, project.CancellationToken)
            ? SourceChange.Unchanged
        : SourceChange.Modified;

    /// <summary>Restricts current files to additions or text changes against this baseline.</summary>
    public CodeQuery<CodeFile> ChangedFiles =>
        Sources.Files.Where(new(file => ChangeOf(file) != SourceChange.Unchanged));

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string DeclarationText(ISymbol symbol, CancellationToken cancellationToken) =>
        string.Join(
            "\n",
            symbol
                .DeclaringSyntaxReferences.OrderBy(
                    reference => reference.SyntaxTree.FilePath,
                    StringComparer.Ordinal
                )
                .ThenBy(reference => reference.Span.Start)
                .Select(reference => reference.GetSyntax(cancellationToken).ToString())
        );

    private static string ProjectKey(AnalysisProject project) =>
        Normalize(project.Snapshot.ProjectPath)
        + "|"
        + project.Snapshot.TargetFramework
        + "|"
        + string.Join(
            "|",
            project
                .Snapshot.Properties.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair =>
                    $"{pair.Key.Length}:{pair.Key.ToUpperInvariant()}{pair.Value.Length}:{pair.Value}"
                )
        );
}
