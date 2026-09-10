using Microsoft.CodeAnalysis;

namespace DrillPress.Queries;

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

    /// <summary>The start of the existing file, suitable for missing-counterpart diagnostics.</summary>
    public SourceLocation Location => Source.Locate(new(0, 0));

    /// <summary>Enumerates syntax in this document, including its root and structured trivia.</summary>
    public IEnumerable<CodeNode<TSyntax>> Nodes<TSyntax>() where TSyntax : SyntaxNode =>
        Source.Tree.GetRoot(Source.Project.CancellationToken).DescendantNodesAndSelf(descendIntoTrivia: true)
            .OfType<TSyntax>().Select(syntax => new CodeNode<TSyntax>(Source, syntax));
}
