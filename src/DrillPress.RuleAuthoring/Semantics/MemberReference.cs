using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A source expression bound to a field, property or method, such as <c>string.Empty</c> or a method group.</summary>
public sealed class MemberReference : ICodeElement
{
    internal MemberReference(AnalysisSource source, ExpressionSyntax syntax, ISymbol symbol)
    {
        Source = source;
        Syntax = syntax;
        Symbol = symbol;
        Expression = new(source, syntax);
    }

    /// <summary>The referenced member's declaring type.</summary>
    public CodeType ContainingType => CodeType.FromSymbol(Symbol.ContainingType);

    /// <summary>The referenced member's metadata name.</summary>
    public string MemberName => Symbol.Name;

    /// <summary>The original document and compilation membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The complete bound expression, such as <c>string.Empty</c> or an unqualified name.</summary>
    public ExpressionSyntax Syntax { get; }

    /// <summary>The resolved member; ambiguous candidate symbols are never selected.</summary>
    public ISymbol Symbol { get; }

    /// <summary>The same occurrence as an expression with type, constant and context helpers.</summary>
    public CodeExpression Expression { get; }

    /// <summary>Source-context evidence such as nameof and expression-tree membership. These facts do not authorize a rewrite.</summary>
    public ExpressionSourceFacts Facts => Expression.Facts;

    /// <summary>The complete reference expression.</summary>
    public SourceLocation Location => Expression.Location;
}
