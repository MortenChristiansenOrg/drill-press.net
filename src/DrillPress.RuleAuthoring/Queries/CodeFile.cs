using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A reportable C# document. Linked memberships and alternate frameworks remain distinct.</summary>
public sealed class CodeFile(AnalysisSource source) : ICodeElement
{
    /// <summary>The document's semantic context.</summary>
    public AnalysisSource Source { get; } = source;

    /// <summary>Captured path with forward slashes, independent of the host operating system.</summary>
    public string Path => Source.Document.Path.Replace('\\', '/');

    /// <summary>The last component of the captured path.</summary>
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];

    /// <summary>The containing path, or an empty string for a bare filename.</summary>
    public string Folder => Path.LastIndexOf('/') is var index && index >= 0 ? Path[..index] : "";

    /// <summary>The path below the project directory, with forward slashes, such as <c>Orders/OrderService.cs</c>; null for a file outside it, such as a linked file. Loose sources are read from the directory analysis was started in.</summary>
    public string? RelativePath => ScopeQueries.RelativePath(Source, null);

    /// <summary>Whether the file contains top-level statements, which make it the program's entry point.</summary>
    public bool HasTopLevelStatements =>
        Source.Tree.GetRoot(Source.Project.CancellationToken) is CompilationUnitSyntax unit
        && unit.Members.OfType<GlobalStatementSyntax>().Any();

    /// <summary>The start of the existing file, suitable for missing-counterpart diagnostics.</summary>
    public SourceLocation Location => Source.Locate(new(0, 0));

    /// <summary>Enumerates syntax in this document, including its root and structured trivia.</summary>
    public IEnumerable<CodeNode<TSyntax>> Nodes<TSyntax>()
        where TSyntax : SyntaxNode =>
        Source.Nodes<TSyntax>().Select(syntax => new CodeNode<TSyntax>(Source, syntax));
}
