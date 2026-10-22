using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A selected written declaration ready for a modifier edit or rename.</summary>
public sealed class DeclarationFix
{
    private readonly AnalysisSource _source;
    private readonly SyntaxNode? _declaration;
    private readonly ISymbol? _symbol;

    internal DeclarationFix(ICodeDeclaration declaration)
    {
        _source = declaration.Source;
        _declaration = DeclarationSyntax.Owner(declaration);
        _symbol = declaration.Symbol;
    }

    /// <summary>Renames a local, parameter, field, property, method or local function and every reference to it, including named arguments, <c>nameof</c> and documentation <c>param</c>/<c>paramref</c> names. <c>Propose()</c> owns the proof for locals, parameters of private methods and local functions, and private members; wider renames need <c>SafeWhen(...)</c>.</summary>
    /// <param name="newName">A valid C# identifier; keywords are escaped with <c>@</c>.</param>
    public SymbolRename Rename(string newName) => new(_source, _symbol, newName);

    /// <summary>Removes one written modifier token, keeping surrounding comments and trivia. Use <c>Propose()</c> for accessibility modifiers whose removal keeps the declared accessibility; other modifiers need <c>SafeWhen(...)</c>.</summary>
    public ModifierRemoval RemoveModifier(Modifier modifier) =>
        new(_source, _declaration, (SyntaxKind)modifier);

    /// <summary>Adds one modifier token in conventional order, such as <c>sealed</c> or <c>static</c>. Use <c>Propose()</c> for <c>sealed</c> and <c>static</c> classes, whose proof the library owns; other modifiers need <c>SafeWhen(...)</c>.</summary>
    public ModifierAddition AddModifier(Modifier modifier) =>
        new(_source, _declaration, (SyntaxKind)modifier);
}
