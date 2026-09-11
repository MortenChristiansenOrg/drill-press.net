using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Analysis;

/// <summary>Shares one lazy semantic model among all rules inspecting a captured document.</summary>
public sealed class AnalysisSource
{
    private readonly Lazy<SemanticModel> _model;

    internal AnalysisSource(AnalysisProject project, DocumentSnapshot document, SyntaxTree tree)
    {
        Project = project;
        Document = document;
        Tree = tree;
        _model = new(() => project.Compilation.GetSemanticModel(tree));
    }

    /// <summary>The independent compilation context containing this document membership.</summary>
    public AnalysisProject Project { get; }

    /// <summary>Original text, byte identity, generated classification, and edit eligibility.</summary>
    public DocumentSnapshot Document { get; }

    /// <summary>The compiler's original syntax tree.</summary>
    public SyntaxTree Tree { get; }

    /// <summary>The cached semantic model, created only when a rule needs binding.</summary>
    public SemanticModel Model => _model.Value;

    /// <summary>Converts a UTF-16 span to physical coordinates, ignoring #line remapping.</summary>
    public SourceLocation Locate(TextSpan span)
    {
        var position = Tree.GetText().Lines.GetLinePosition(span.Start);
        return new(
            Document.Path,
            span.Start,
            span.Length,
            position.Line + 1,
            position.Character + 1
        );
    }
}
