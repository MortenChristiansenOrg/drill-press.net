using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>
/// Where an element runs relative to the try statements around it in its own function. A try statement guards its try
/// block when one of its catch clauses catches any exception; catch filters are not evaluated.
/// </summary>
public static class HandlerQueries
{
    /// <summary>Whether a try statement around the element, in the function that contains it, guards it with a catch clause that catches any exception.</summary>
    public static bool IsGuardedByCatchAll(this ICodeElement element)
    {
        var syntax = ElementSyntax.Of(element);
        foreach (var ancestor in syntax.Ancestors())
        {
            if (ElementSyntax.IsFunction(ancestor))
                return false;
            if (ancestor is TryStatementSyntax attempt && Guards(attempt, syntax, element.Source))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The catch or finally clause the element runs in, unless a try statement inside that clause guards it with a catch
    /// clause that catches any exception. A failure there replaces the exception being handled, or escapes the handler.
    /// </summary>
    /// <remarks>None outside handlers and inside a lambda or local function, which runs at another time.</remarks>
    public static HandlerClause UnguardedHandler(this ICodeElement element)
    {
        var syntax = ElementSyntax.Of(element);
        foreach (var ancestor in syntax.Ancestors())
        {
            if (ElementSyntax.IsFunction(ancestor))
                return HandlerClause.None;
            switch (ancestor)
            {
                case TryStatementSyntax attempt when Guards(attempt, syntax, element.Source):
                    return HandlerClause.None;
                case CatchClauseSyntax:
                    return HandlerClause.Catch;
                case FinallyClauseSyntax:
                    return HandlerClause.Finally;
            }
        }
        return HandlerClause.None;
    }

    private static bool Guards(
        TryStatementSyntax attempt,
        SyntaxNode node,
        AnalysisSource source
    ) =>
        attempt.Block.Span.Contains(node.Span)
        && attempt.Catches.Any(clause => new CodeCatch(source, clause).CatchesAnyException);
}
