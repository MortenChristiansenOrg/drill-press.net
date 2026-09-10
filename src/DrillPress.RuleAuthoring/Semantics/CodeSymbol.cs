using Microsoft.CodeAnalysis;

namespace DrillPress.Semantics;

/// <summary>A resolved source declaration or reference, including declarations beyond ordinary methods and named types.</summary>
public sealed class CodeSymbol(AnalysisSource source, SyntaxNode syntax, ISymbol symbol) : ICodeElement
{
    /// <summary>The original compilation and document membership.</summary>
    public AnalysisSource Source { get; } = source;

    /// <summary>The declaration or bound reference syntax.</summary>
    public SyntaxNode Syntax { get; } = syntax;

    /// <summary>The compiler-resolved identity; ambiguous candidate symbols are not guessed.</summary>
    public ISymbol Symbol { get; } = symbol;

    /// <summary>The complete syntax span, overridable by a rule's location selector.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Span);
}
