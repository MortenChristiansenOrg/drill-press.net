using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>One distinct named type definition, with a deterministic ordinary-source declaration.</summary>
public sealed class CodeDeclaration : ICodeElement
{
    internal CodeDeclaration(
        AnalysisSolution solution,
        AnalysisSource source,
        MemberDeclarationSyntax syntax,
        INamedTypeSymbol symbol
    )
    {
        Solution = solution;
        Source = source;
        Syntax = syntax;
        Symbol = symbol;
        Location = source.Locate(
            syntax switch
            {
                BaseTypeDeclarationSyntax type => type.Identifier.Span,
                DelegateDeclarationSyntax declaration => declaration.Identifier.Span,
                _ => throw new ArgumentException(
                    "A type candidate requires a named type declaration.",
                    nameof(syntax)
                ),
            }
        );
    }

    /// <summary>The shared graph used by cross-project conditions.</summary>
    public AnalysisSolution Solution { get; }

    /// <summary>The selected ordinary source declaration's context.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The selected declaration; partial declarations share one candidate.</summary>
    public MemberDeclarationSyntax Syntax { get; }

    /// <summary>The bound original type definition.</summary>
    public INamedTypeSymbol Symbol { get; }

    /// <summary>The unqualified declared type name.</summary>
    public string Name => Symbol.Name;

    /// <summary>The declared namespace, or an empty string for the global namespace.</summary>
    public string Namespace =>
        Symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : Symbol.ContainingNamespace.ToDisplayString();

    /// <summary>Tests all inherited and constructed interfaces by semantic identity.</summary>
    public bool Implements(CodeType contract) => Symbols.Implements(Symbol, contract);

    /// <summary>Tests semantic attributes, including derived attribute types.</summary>
    public bool HasAttribute(CodeType attribute) => Symbols.HasAttribute(Symbol, attribute);

    /// <summary>Matches an attribute's marker identity and compiler-recorded values.</summary>
    public bool HasAttribute(CodeType attribute, Func<CodeAttribute, bool> where) =>
        Symbol.HasAttribute(attribute, where);

    /// <summary>Tests the strict base-class chain, excluding this definition.</summary>
    public bool DerivesFrom(CodeType type) => Symbol.DerivesFrom(type);

    /// <summary>Tests this definition and its base-class chain.</summary>
    public bool IsOrDerivesFrom(CodeType type) => Symbol.IsOrDerivesFrom(type);

    /// <summary>The selected type identifier's physical span.</summary>
    public SourceLocation Location { get; }
}
