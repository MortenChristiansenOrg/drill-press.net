using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>One distinct named type definition, with a deterministic ordinary-source declaration.</summary>
public sealed class CodeDeclaration : ICodeElement
{
    internal CodeDeclaration(AnalysisSolution solution, AnalysisSource source, MemberDeclarationSyntax syntax, INamedTypeSymbol symbol)
    {
        Solution = solution;
        Source = source;
        Syntax = syntax;
        Symbol = symbol;
        Location = source.Locate(syntax switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier.Span,
            DelegateDeclarationSyntax declaration => declaration.Identifier.Span,
            _ => throw new ArgumentException("A type candidate requires a named type declaration.", nameof(syntax)),
        });
    }

    /// <summary>The shared graph used by cross-project conditions.</summary>
    public AnalysisSolution Solution { get; }

    /// <summary>The selected ordinary source declaration's context.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The selected declaration; partial declarations share one candidate.</summary>
    public MemberDeclarationSyntax Syntax { get; }

    /// <summary>The bound original type definition.</summary>
    public INamedTypeSymbol Symbol { get; }

    /// <summary>The selected type identifier's physical span.</summary>
    public SourceLocation Location { get; }
}
