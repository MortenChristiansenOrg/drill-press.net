using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>Source-context evidence for an expression. Facts describe syntax and never authorize a rewrite by themselves.</summary>
public sealed class ExpressionSourceFacts
{
    private readonly AnalysisSource _source;
    private readonly ExpressionSyntax _syntax;

    internal ExpressionSourceFacts(AnalysisSource source, ExpressionSyntax syntax)
    {
        _source = source;
        _syntax = syntax;
    }

    /// <summary>Whether an enclosing lambda is converted to a framework expression tree.</summary>
    public bool IsInsideExpressionTree =>
        _syntax
            .AncestorsAndSelf()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .Any(lambda =>
                _source.Model.GetTypeInfo(lambda, _source.Project.CancellationToken).ConvertedType
                    is { } type
                && CodeType.Framework("System.Linq.Expressions.Expression<>").Matches(type)
            );

    /// <summary>Whether the compiler places this expression within a nameof operand.</summary>
    public bool IsInsideNameOf =>
        _syntax
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(call =>
                _source.Model.GetOperation(call, _source.Project.CancellationToken)
                is INameOfOperation
            );

    /// <summary>Whether the replaced span contains ordinary or documentation comments, excluding exterior trivia.</summary>
    public bool HasComments =>
        Interior.Any(trivia =>
            trivia.Kind()
                is SyntaxKind.SingleLineCommentTrivia
                    or SyntaxKind.MultiLineCommentTrivia
                    or SyntaxKind.SingleLineDocumentationCommentTrivia
                    or SyntaxKind.MultiLineDocumentationCommentTrivia
        );

    /// <summary>Whether preprocessor directives occur within the expression span.</summary>
    public bool HasDirectives => Interior.Any(trivia => trivia.IsDirective);

    /// <summary>Whether inactive preprocessor text occurs within the expression span.</summary>
    public bool HasDisabledText =>
        Interior.Any(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia));
    private IEnumerable<SyntaxTrivia> Interior =>
        _syntax
            .DescendantTrivia(descendIntoTrivia: true)
            .Where(trivia => _syntax.Span.Contains(trivia.Span));
}
