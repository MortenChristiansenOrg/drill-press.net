using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A selected written declaration ready for a modifier edit.</summary>
public sealed class DeclarationFix
{
    private readonly AnalysisSource _source;
    private readonly SyntaxNode? _declaration;

    internal DeclarationFix(ICodeDeclaration declaration)
    {
        _source = declaration.Source;
        _declaration = DeclarationSyntax.Owner(declaration);
    }

    /// <summary>Removes one written modifier token, keeping surrounding comments and trivia. Use <c>Propose()</c> for accessibility modifiers whose removal keeps the declared accessibility; other modifiers need <c>SafeWhen(...)</c>.</summary>
    public ModifierRemoval RemoveModifier(Modifier modifier) =>
        new(_source, _declaration, (SyntaxKind)modifier);
}
