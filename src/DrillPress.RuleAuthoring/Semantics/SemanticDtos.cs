namespace DrillPress.Semantics;

/// <summary>Describes a source expression bound to a member on a specific CLR type.</summary>
/// <param name="ContainingType">The declaring type resolved by semantic analysis.</param>
/// <param name="MemberName">The metadata name of the referenced member.</param>
/// <param name="Location">The complete source expression to report.</param>
public sealed record MemberReference(
    CodeType ContainingType,
    string MemberName,
    SourceLocation Location) : ICodeElement
{
    /// <summary>Semantic context, absent for manually supplied candidates.</summary>
    public AnalysisSource? Source { get; init; }

    /// <summary>The complete bound expression when discovered from a compilation.</summary>
    public Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax? Syntax { get; init; }

    /// <summary>The resolved member; candidate symbols from ambiguous binding are excluded.</summary>
    public Microsoft.CodeAnalysis.ISymbol? Symbol { get; init; }
}
