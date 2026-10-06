using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>One written type declaration, retaining each partial part and unresolved declaration separately.</summary>
public sealed class CodeTypeDeclaration : ICodeElement
{
    internal CodeTypeDeclaration(AnalysisSource source, MemberDeclarationSyntax syntax)
    {
        Source = source;
        Syntax = syntax;
    }

    /// <summary>The actual declaration part's document and evaluated compilation.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The written class, struct, record, interface, enum or delegate declaration.</summary>
    public MemberDeclarationSyntax Syntax { get; }

    /// <summary>The bound type when available; all parts of a partial type share this symbol.</summary>
    public INamedTypeSymbol? Symbol =>
        Source.Model.GetDeclaredSymbol(Syntax, Source.Project.CancellationToken)
        as INamedTypeSymbol;

    /// <summary>The part's identifier.</summary>
    public string Name => Identifier.ValueText;

    /// <summary>Identifier words preserving casing, including acronym and digit boundaries.</summary>
    public IReadOnlyList<string> NameWords => CodeIdentifier.NamedWords(Name);

    /// <summary>Whether the part is declared at compilation or namespace scope.</summary>
    public bool IsTopLevel =>
        Syntax.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax;

    /// <summary>The type-name reporting span in this actual part.</summary>
    public SourceLocation Location => Source.Locate(Identifier.Span);

    /// <summary>Tests the written modifier token, independently of effective/default accessibility.</summary>
    public bool HasExplicitModifier(Modifier modifier) =>
        DeclarationSyntax.Modifiers(Syntax).Any((SyntaxKind)modifier);

    private SyntaxToken Identifier =>
        Syntax is BaseTypeDeclarationSyntax type
            ? type.Identifier
            : ((DelegateDeclarationSyntax)Syntax).Identifier;
}
