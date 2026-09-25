using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.SampleRules.Fixes;

internal static class RewriteSyntax
{
    public static bool HasInteriorContent(SyntaxNode node) =>
        node.DescendantTrivia(descendIntoTrivia: true)
            .Any(trivia =>
                node.Span.Contains(trivia.Span)
                && !trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                && !trivia.IsKind(SyntaxKind.EndOfLineTrivia)
            );

    public static bool IsObservableSyntax(AnalysisSource source, SyntaxNode node) =>
        node.AncestorsAndSelf()
            .Any(ancestor =>
                ancestor
                    is InvocationExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }
                    }
                || ancestor is AnonymousFunctionExpressionSyntax lambda
                    && source.Model.GetTypeInfo(lambda).ConvertedType is INamedTypeSymbol type
                    && CodeType.Named("System.Linq.Expressions.Expression<>").Matches(type)
            );
}
