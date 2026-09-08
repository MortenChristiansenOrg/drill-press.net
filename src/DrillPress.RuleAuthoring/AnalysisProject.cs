using DrillPress.Manifest;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress;

/// <summary>One evaluated compilation context; alternate target frameworks remain separate.</summary>
public sealed class AnalysisProject
{
    private readonly Lazy<IReadOnlyList<AnalysisSource>> _sources;

    /// <summary>Pairs a compiler-faithful snapshot with its live or reconstructed compilation.</summary>
    public AnalysisProject(ProjectSnapshot snapshot, CSharpCompilation compilation, CancellationToken cancellationToken = default)
    {
        var trees = compilation.SyntaxTrees.ToArray();
        if (trees.Length != snapshot.Documents.Length || trees.Where((tree, index) => !snapshot.Documents[index].IsGenerated && tree.FilePath != snapshot.Documents[index].Path).Any())
        {
            throw new ArgumentException("Compilation trees must match the snapshot's ordered document memberships.", nameof(compilation));
        }

        CancellationToken = cancellationToken;
        Snapshot = snapshot;
        Compilation = compilation;
        _sources = new(() => trees.Zip(snapshot.Documents,
            (tree, document) => new AnalysisSource(this, document, tree)).ToArray());
    }

    /// <summary>Stops candidate discovery and contextual compiler validation.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Evaluated context identity, graph edges, and document metadata.</summary>
    public ProjectSnapshot Snapshot { get; }

    /// <summary>The compiler used for semantic identity and contextual rewrite validation.</summary>
    public CSharpCompilation Compilation { get; }

    /// <summary>Lazily pairs every syntax tree, including generated trees, with its captured source.</summary>
    public IReadOnlyList<AnalysisSource> Sources => _sources.Value;
}
