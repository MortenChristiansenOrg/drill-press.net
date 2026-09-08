using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>Physical test-body facts shared by whitespace and assertion conventions.</summary>
public sealed class TestBody
{
    internal TestBody(CodeMethod method)
    {
        if (method.Syntax.Body is not { } body)
        {
            EmptyLines = [];
            Assertions = [];
            return;
        }

        var excluded = body.DescendantNodes().Select(NestedBody).OfType<TextSpan>().ToList();
        excluded.AddRange(body.DescendantTokens().Where(token => token.Text.Contains('\n') || token.Text.Contains('\r'))
            .Select(token => token.Span));
        excluded.AddRange(body.DescendantTrivia(descendIntoTrivia: true)
            .Where(trivia => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.DisabledTextTrivia)).Select(trivia => trivia.Span));
        EmptyLines = method.Source.Tree.GetText().Lines
            .Where(line => line.Start > body.OpenBraceToken.Span.End && line.End < body.CloseBraceToken.Span.Start &&
                string.IsNullOrWhiteSpace(line.ToString()) && !excluded.Any(span => span.IntersectsWith(line.SpanIncludingLineBreak)))
            .Select(line => method.Source.Locate(new TextSpan(line.Start, line.Span.Length))).ToArray();
        Assertions = body.DescendantNodes(node => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => (Invocation: invocation, Symbol: method.Source.Model.GetSymbolInfo(invocation, method.Source.Project.CancellationToken).Symbol as IMethodSymbol))
            .Where(item => item.Symbol is { ContainingType: { } type } && XunitTests.IsXunitType(type, "Xunit.Assert"))
            .Select(item => new TestAssertion(item.Symbol!.Name, method.Source.Locate(item.Invocation.Span))).ToArray();
    }

    /// <summary>Whitespace-only physical lines outside multiline content and nested functions.</summary>
    public IReadOnlyList<SourceLocation> EmptyLines { get; }

    /// <summary>Resolved xUnit assertions in source order, excluding nested functions.</summary>
    public IReadOnlyList<TestAssertion> Assertions { get; }

    /// <summary>The first assertion before the final blank line, except a sole synchronous Throws.</summary>
    public SourceLocation? EarlyAssertion => EmptyLines.Count == 0 || Assertions is [{ MemberName: "Throws" }]
        ? null : Assertions.FirstOrDefault(assertion => assertion.Location.Start < EmptyLines[^1].Start)?.Location;
    private static TextSpan? NestedBody(SyntaxNode node) => node switch
    {
        LocalFunctionStatementSyntax local => local.Body?.Span ?? local.ExpressionBody?.Expression.Span,
        LambdaExpressionSyntax lambda => lambda.Body.Span,
        AnonymousMethodExpressionSyntax anonymous => anonymous.Block.Span,
        _ => null,
    };
}
