using DrillPress.Manifest;
using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Low-level evidence for one transformation after the complete atomic batch has been applied in one compilation: original and rewritten syntax, semantic models and kept-operand mappings.</summary>
public sealed class RewriteEvidence
{
    internal RewriteEvidence(
        RewriteContext context,
        NodeRewrite target,
        IReadOnlyList<InputRewrite> inputs,
        IReadOnlyList<SourceEdit>? ownEdits = null
    )
    {
        Context = context;
        Target = target;
        Inputs = inputs;
        OwnEdits = ownEdits;
    }

    /// <summary>The edits of the proposal being proved, when known; other edits in <see cref="RewriteContext.Edits"/> belong to proposals validated together with it, each proving its own changes.</summary>
    internal IReadOnlyList<SourceEdit>? OwnEdits { get; }

    /// <summary>The complete rewritten compilation and original-text edits.</summary>
    public RewriteContext Context { get; }

    /// <summary>The selected before/after node and rewritten semantic model.</summary>
    public NodeRewrite Target { get; }

    /// <summary>The actual original membership being validated, including linked contexts without findings.</summary>
    public AnalysisSource Source => Target.Source;

    /// <summary>The selected original node in this compilation.</summary>
    public SyntaxNode Before => Target.Before;

    /// <summary>The mapped replacement in this compilation.</summary>
    public SyntaxNode After => Target.After;

    /// <summary>The original semantic model.</summary>
    public SemanticModel BeforeModel => Source.Model;

    /// <summary>The semantic model after all proposed edits.</summary>
    public SemanticModel AfterModel => Target.Model;

    /// <summary>Explicit retained-operand mappings, excluding unregistered syntax.</summary>
    public IReadOnlyList<InputRewrite> Inputs { get; }
}
