using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Semantics;

/// <summary>Opt-in, bounded member-key grammars. Custom serialized names and indices can use a parser delegate instead.</summary>
public static class MemberKeyGrammar
{
    /// <summary>Parses dotted C# identifiers and constant int/string index lists, such as Items[0].Name. Calls, dynamic indices, conditional access and arbitrary expressions are rejected.</summary>
    public static IReadOnlyList<MemberKeySegment>? DottedAndIndexed(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        var expression = SyntaxFactory.ParseExpression("_model." + key);
        if (
            expression.ContainsDiagnostics
            || expression
                .DescendantTrivia()
                .Any(trivia => !string.IsNullOrWhiteSpace(trivia.ToString()))
        )
            return null;
        var segments = new List<MemberKeySegment>();
        ExpressionSyntax current = expression;
        while (true)
        {
            switch (current)
            {
                case MemberAccessExpressionSyntax { Name: IdentifierNameSyntax member } access
                    when access.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                    segments.Add(new(member.Identifier.ValueText, []));
                    current = access.Expression;
                    break;
                case ElementAccessExpressionSyntax element:
                    var indices = element
                        .ArgumentList.Arguments.Select(argument => Index(argument.Expression))
                        .ToArray();
                    if (
                        indices.Length == 0
                        || indices.Any(index => index is null)
                        || element.ArgumentList.Arguments.Any(argument =>
                            argument.NameColon is not null || argument.RefKindKeyword.RawKind != 0
                        )
                    )
                        return null;
                    segments.Add(new(null, indices.OfType<MemberKeyIndex>().ToArray()));
                    current = element.Expression;
                    break;
                case IdentifierNameSyntax { Identifier.ValueText: "_model" }:
                    segments.Reverse();
                    return segments.AsReadOnly();
                default:
                    return null;
            }
        }
    }

    private static MemberKeyIndex? Index(ExpressionSyntax expression) =>
        expression switch
        {
            LiteralExpressionSyntax { Token.Value: int value } => new(
                SpecialType.System_Int32,
                value
            ),
            LiteralExpressionSyntax { Token.Value: string value } => new(
                SpecialType.System_String,
                value
            ),
            PrefixUnaryExpressionSyntax
            {
                Operand: LiteralExpressionSyntax { Token.Value: int value }
            } unary when unary.IsKind(SyntaxKind.UnaryMinusExpression) => new(
                SpecialType.System_Int32,
                -value
            ),
            _ => null,
        };
}
