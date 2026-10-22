using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>Shares one lazy semantic model among all rules inspecting a captured document.</summary>
public sealed class AnalysisSource
{
    private readonly Lazy<SemanticModel> _model;
    private readonly Lazy<LocalAssignments> _localAssignments;
    private readonly Lazy<IReadOnlyList<CodeObjectCreation>> _objectCreations;

    internal AnalysisSource(AnalysisProject project, DocumentSnapshot document, SyntaxTree tree)
    {
        Project = project;
        Document = document;
        Tree = tree;
        _model = new(() => project.Compilation.GetSemanticModel(tree));
        _localAssignments = new(() => new LocalAssignments(this));
        Operations = new(this);
        _objectCreations = new(() =>
            Array.AsReadOnly(
                tree.GetRoot(project.CancellationToken)
                    .DescendantNodes()
                    .OfType<BaseObjectCreationExpressionSyntax>()
                    .Select(syntax => new CodeObjectCreation(this, syntax))
                    .ToArray()
            )
        );
        Errors = new(this);
    }

    /// <summary>The independent compilation context containing this document membership.</summary>
    public AnalysisProject Project { get; }

    /// <summary>Original text, byte identity, generated classification, and edit eligibility.</summary>
    public DocumentSnapshot Document { get; }

    /// <summary>The compiler's original syntax tree.</summary>
    public SyntaxTree Tree { get; }

    /// <summary>The cached semantic model, created only when a rule needs binding.</summary>
    public SemanticModel Model => _model.Value;

    internal LocalAssignments LocalAssignments => _localAssignments.Value;

    internal SourceOperations Operations { get; }

    // Shared, so that every query sees the same creations and binds each one once.
    internal IReadOnlyList<CodeObjectCreation> ObjectCreations => _objectCreations.Value;

    internal SourceErrors Errors { get; }

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
