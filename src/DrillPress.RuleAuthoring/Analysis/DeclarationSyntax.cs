using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

internal static class DeclarationSyntax
{
    internal static SyntaxTokenList Modifiers(SyntaxNode node) =>
        node switch
        {
            BaseTypeDeclarationSyntax type => type.Modifiers,
            DelegateDeclarationSyntax declaration => declaration.Modifiers,
            BaseMethodDeclarationSyntax method => method.Modifiers,
            BasePropertyDeclarationSyntax property => property.Modifiers,
            BaseFieldDeclarationSyntax field => field.Modifiers,
            AccessorDeclarationSyntax accessor => accessor.Modifiers,
            LocalFunctionStatementSyntax function => function.Modifiers,
            ParameterSyntax parameter => parameter.Modifiers,
            VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax field } =>
                field.Modifiers,
            _ => default,
        };

    internal static bool HasModifier(SyntaxNode node, Modifier modifier) =>
        Modifiers(node).Any((SyntaxKind)modifier);

    internal static IReadOnlyList<ISymbol>? Symbols(SemanticModel model, SyntaxNode node)
    {
        var declarations = node is BaseFieldDeclarationSyntax field
            ? field.Declaration.Variables.Cast<SyntaxNode>()
            : [node];
        var symbols = declarations
            .Select(declaration => model.GetDeclaredSymbol(declaration))
            .ToArray();
        return symbols.Any(symbol => symbol is null) ? null : symbols.OfType<ISymbol>().ToArray();
    }
}
