using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>An expression-fix selection that remains unavailable when the member reference lacks original source.</summary>
public sealed class ReferenceFix
{
    private readonly FixBuilder? _builder;

    internal ReferenceFix(FixBuilder? builder) => _builder = builder;

    /// <summary>Constructs a literal or other expression replacement, retaining unavailable selection state.</summary>
    public ReferenceFix ReplaceWith(ExpressionSyntax expression) =>
        new(_builder?.ReplaceWith(expression));

    /// <summary>Constructs a template replacement with automatic input correspondence.</summary>
    public ReferenceFix ReplaceWith(ExpressionTemplate expression) =>
        new(_builder?.ReplaceWith(expression));

    /// <summary>Registers original input occurrences.</summary>
    public ReferenceFix Keeping(params ExpressionInput[] inputs) => new(_builder?.Keeping(inputs));

    /// <summary>Adds bounded expression invariants.</summary>
    public ReferenceFix MustPreserve(Behavior behavior) => new(_builder?.MustPreserve(behavior));

    /// <summary>Adds a contextual invariant; it does not supply behavioral equivalence.</summary>
    public ReferenceFix Require(Func<RewriteEvidence, ProofResult> check) =>
        new(_builder?.Require(check));

    /// <summary>Supplies the required contextual behavior proof; unavailable references produce no proposal.</summary>
    public FixProposal? Propose(Func<RewriteEvidence, ProofResult> proof) =>
        _builder?.Propose(proof);

    /// <summary>Supplies a Boolean proof, with false treated as Unknown.</summary>
    public FixProposal? SafeWhen(Func<RewriteEvidence, bool> proof) => _builder?.SafeWhen(proof);

    /// <summary>Supplies a tri-state behavior proof.</summary>
    public FixProposal? SafeWhen(Func<RewriteEvidence, ProofResult> proof) =>
        _builder?.SafeWhen(proof);
}
