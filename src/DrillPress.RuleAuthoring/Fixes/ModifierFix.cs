using DrillPress.Queries;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Fixes;

/// <summary>Conservative modifier removal, proved against the compiler's resulting declaration accessibility in every affected context.</summary>
public static class ModifierFix
{
    /// <summary>Removes a sole internal or private access token from a named type, method or property only when its effective accessibility remains identical.</summary>
    public static FixProposal? RemoveRedundantAccessibility(CodeNode<MemberDeclarationSyntax> candidate)
    {
        var modifiers = Modifiers(candidate.Syntax);
        var access = modifiers.Where(token => token.IsKind(SyntaxKind.PublicKeyword) || token.IsKind(SyntaxKind.PrivateKeyword) ||
            token.IsKind(SyntaxKind.ProtectedKeyword) || token.IsKind(SyntaxKind.InternalKeyword) || token.IsKind(SyntaxKind.FileKeyword)).ToArray();
        if (access.Length != 1 || access[0].Kind() is not (SyntaxKind.InternalKeyword or SyntaxKind.PrivateKeyword))
        {
            return null;
        }

        var token = access[0];
        var removedLength = token.Span.Length + token.TrailingTrivia.TakeWhile(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia)).Sum(trivia => trivia.FullSpan.Length);
        var span = new Microsoft.CodeAnalysis.Text.TextSpan(token.SpanStart, removedLength);
        var edit = SourceChanges.Replace(candidate.Source, span, "");
        return SourceChanges.Propose([edit], context => context.Original.Sources.Where(source => source.Document.FileIdentity == edit.FileIdentity)
            .All(source => SameAccessibility(source, context, candidate.Syntax.SpanStart, edit.Length)));
    }

    private static bool SameAccessibility(AnalysisSource source, RewriteContext context, int start, int removedLength)
    {
        var before = source.Tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(node => node.SpanStart == start && Modifiers(node).Count > 0);
        var tree = context.Rewritten.SyntaxTrees.Single(tree => tree.FilePath == source.Tree.FilePath);
        var after = before is null ? null : tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(node => node.RawKind == before.RawKind && node.Span.End == before.Span.End - removedLength);
        var oldSymbol = before is null ? null : source.Model.GetDeclaredSymbol(before);
        var newSymbol = after is null ? null : context.Rewritten.GetSemanticModel(tree).GetDeclaredSymbol(after);
        return oldSymbol is not null && newSymbol is not null && oldSymbol.DeclaredAccessibility == newSymbol.DeclaredAccessibility &&
            oldSymbol.GetDocumentationCommentId() == newSymbol.GetDocumentationCommentId();
    }

    private static SyntaxTokenList Modifiers(MemberDeclarationSyntax node) => node switch
    {
        TypeDeclarationSyntax type => type.Modifiers,
        MethodDeclarationSyntax method => method.Modifiers,
        PropertyDeclarationSyntax property => property.Modifiers,
        _ => default,
    };
}
