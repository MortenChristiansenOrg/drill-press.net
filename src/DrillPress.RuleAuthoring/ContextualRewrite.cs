using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

internal static class ContextualRewrite
{
    private static readonly SymbolDisplayFormat _signature = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMemberOptions(SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeParameters |
            SymbolDisplayMemberOptions.IncludeType | SymbolDisplayMemberOptions.IncludeRef)
        .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut)
        .WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public static bool HasInteriorContent(SyntaxNode node) => node.DescendantTrivia(descendIntoTrivia: true)
        .Any(trivia => node.Span.Contains(trivia.Span) && !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia));

    public static bool IsObservableSyntax(AnalysisSource source, SyntaxNode node) => node.AncestorsAndSelf().Any(ancestor =>
        ancestor is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } ||
        ancestor is AnonymousFunctionExpressionSyntax lambda && source.Model.GetTypeInfo(lambda).ConvertedType is INamedTypeSymbol type &&
        CodeType.MetadataNameOf(type) == "System.Linq.Expressions.Expression`1");

    public static bool PreservesBinding(AnalysisSource source, SyntaxNode replaced, string replacement,
        InvocationExpressionSyntax? changedCall = null)
    {
        if (IsObservableSyntax(source, replaced))
        {
            return false;
        }

        var originalText = source.Tree.GetText();
        var changedTree = source.Tree.WithChangedText(originalText.WithChanges(new TextChange(replaced.Span, replacement)));
        var changedCompilation = source.Project.Compilation.ReplaceSyntaxTree(source.Tree, changedTree);
        if (source.Project.Compilation.Options.SyntaxTreeOptionsProvider is { } provider)
        {
            changedCompilation = changedCompilation.WithOptions(changedCompilation.Options.WithSyntaxTreeOptionsProvider(
                new RewrittenTreeOptions(provider, source.Tree, changedTree)));
        }

        var changedModel = changedCompilation.GetSemanticModel(changedTree);
        var delta = replacement.Length - replaced.Span.Length;
        var scope = replaced.AncestorsAndSelf().FirstOrDefault(node => node is StatementSyntax or MemberDeclarationSyntax) ?? replaced;
        if (source.Model.GetDiagnostics(scope.Span, source.Project.CancellationToken).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ||
            changedModel.GetDiagnostics(new TextSpan(scope.SpanStart, scope.Span.Length + delta), source.Project.CancellationToken)
                .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        foreach (var expression in replaced.AncestorsAndSelf().OfType<ExpressionSyntax>())
        {
            var newSpan = new TextSpan(expression.SpanStart, expression.Span.Length + delta);
            var rewritten = changedTree.GetRoot().FindNode(newSpan, getInnermostNodeForTie: true) as ExpressionSyntax;
            if (rewritten is null || rewritten.Span != newSpan || !SameTypeAndConversion(source.Model, expression, changedModel, rewritten))
            {
                return false;
            }

            if (expression == changedCall)
            {
                if (!OrdinalComparerFix.IsDistinct(changedModel.GetSymbolInfo(rewritten).Symbol as IMethodSymbol, 1))
                {
                    return false;
                }
            }
            else if (expression != replaced)
            {
                var before = source.Model.GetSymbolInfo(expression);
                var after = changedModel.GetSymbolInfo(rewritten);
                if (before.CandidateReason != CandidateReason.None || after.CandidateReason != CandidateReason.None ||
                    Identity(before.Symbol) != Identity(after.Symbol))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool SameTypeAndConversion(SemanticModel beforeModel, ExpressionSyntax before,
        SemanticModel afterModel, ExpressionSyntax after)
    {
        var oldType = beforeModel.GetTypeInfo(before);
        var newType = afterModel.GetTypeInfo(after);
        var oldConversion = beforeModel.GetConversion(before);
        var newConversion = afterModel.GetConversion(after);
        return oldType.Type?.TypeKind != TypeKind.Error && newType.Type?.TypeKind != TypeKind.Error &&
            Identity(oldType.Type) == Identity(newType.Type) && Identity(oldType.ConvertedType) == Identity(newType.ConvertedType) &&
            oldConversion.IsIdentity == newConversion.IsIdentity && oldConversion.IsImplicit == newConversion.IsImplicit &&
            oldConversion.IsNumeric == newConversion.IsNumeric && oldConversion.IsUserDefined == newConversion.IsUserDefined &&
            Identity(oldConversion.MethodSymbol) == Identity(newConversion.MethodSymbol);
    }

    private static string? Identity(ISymbol? symbol) => symbol is null ? null :
        symbol.ContainingAssembly?.Identity + ":" + symbol.ToDisplayString(_signature);
}
