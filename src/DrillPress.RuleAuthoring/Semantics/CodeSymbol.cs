using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A resolved source declaration or reference of any symbol kind, for kinds without a dedicated element such as events, accessors and locals.</summary>
public sealed class CodeSymbol(AnalysisSource source, SyntaxNode syntax, ISymbol symbol)
    : ICodeDeclaration
{
    /// <summary>The original compilation and document membership.</summary>
    public AnalysisSource Source { get; } = source;

    /// <summary>The declaration or bound reference syntax.</summary>
    public SyntaxNode Syntax { get; } = syntax;

    /// <summary>The compiler-resolved identity; ambiguous candidate symbols are not guessed.</summary>
    public ISymbol Symbol { get; } = symbol;

    ISymbol? ICodeDeclaration.Symbol => Symbol;

    /// <summary>The symbol's unqualified name.</summary>
    public string Name => Symbol.Name;

    /// <summary>Identifier words preserving casing, including acronym and digit boundaries.</summary>
    public IReadOnlyList<string> NameWords => CodeIdentifier.NamedWords(Name);

    /// <summary>The symbol's declared accessibility.</summary>
    public Accessibility Accessibility => Symbol.DeclaredAccessibility;

    /// <summary>Whether the written declaration contains this modifier token; references have none.</summary>
    public bool HasExplicitModifier(Modifier modifier) =>
        DeclarationSyntax.HasModifier(Syntax, modifier);

    /// <summary>The declared identifier for declarations, otherwise the complete reference syntax.</summary>
    public SourceLocation Location => Source.Locate(Identifier(Syntax)?.Span ?? Syntax.Span);

    private static SyntaxToken? Identifier(SyntaxNode node) =>
        node switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier,
            DelegateDeclarationSyntax declaration => declaration.Identifier,
            MethodDeclarationSyntax method => method.Identifier,
            ConstructorDeclarationSyntax constructor => constructor.Identifier,
            DestructorDeclarationSyntax destructor => destructor.Identifier,
            PropertyDeclarationSyntax property => property.Identifier,
            EventDeclarationSyntax declaration => declaration.Identifier,
            VariableDeclaratorSyntax variable => variable.Identifier,
            ParameterSyntax parameter => parameter.Identifier,
            TypeParameterSyntax parameter => parameter.Identifier,
            LocalFunctionStatementSyntax function => function.Identifier,
            EnumMemberDeclarationSyntax member => member.Identifier,
            SingleVariableDesignationSyntax designation => designation.Identifier,
            _ => null,
        };
}
