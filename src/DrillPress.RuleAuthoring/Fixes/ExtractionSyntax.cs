using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

internal static class ExtractionSyntax
{
    internal static bool Eligible(AnalysisSource source, SyntaxNode node) =>
        source.Document.IsEditable
        && !source.Document.IsGenerated
        && node.SyntaxTree == source.Tree
        && !node.ContainsDiagnostics
        && !node.ContainsDirectives
        && !ContextualRewrite.HasInteriorContent(node)
        && !ContextualRewrite.IsObservableSyntax(source, node)
        && !node.AncestorsAndSelf()
            .Any(ancestor =>
                ancestor
                    is CheckedExpressionSyntax
                        or CheckedStatementSyntax
                        or UnsafeStatementSyntax
                || ancestor.ChildTokens().Any(token => token.IsKind(SyntaxKind.UnsafeKeyword))
            );

    internal static bool Denotable(ITypeSymbol type) =>
        type switch
        {
            IArrayTypeSymbol array => Denotable(array.ElementType),
            ITypeParameterSymbol parameter => parameter.TypeParameterKind == TypeParameterKind.Type,
            INamedTypeSymbol named => !named.IsAnonymousType
                && !named.IsRefLikeType
                && named.TypeKind != TypeKind.Error
                && named.TypeArguments.All(Denotable),
            _ => false,
        };

    internal static TypeSyntax TypeName(ITypeSymbol type) =>
        SyntaxFactory.ParseTypeName(
            type.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                    SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                        | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                )
            )
        );

    internal static ExpressionSyntax? Constant(ITypeSymbol type, object? value)
    {
        if (
            value
            is not (
                null
                or string
                or char
                or bool
                or sbyte
                or byte
                or short
                or ushort
                or int
                or uint
                or long
                or ulong
                or float
                or double
                or decimal
            )
        )
            return null;
        var expression = LiteralSyntax.Create(value);
        return type.TypeKind == TypeKind.Enum
            ? SyntaxFactory.CastExpression(
                TypeName(type),
                SyntaxFactory.ParenthesizedExpression(expression)
            )
            : expression;
    }

    internal static (int Position, string Text) Insert(
        AnalysisSource source,
        TypeDeclarationSyntax part,
        MemberDeclarationSyntax member
    )
    {
        var text = source.Tree.GetText(source.Project.CancellationToken);
        var first = text.Lines.GetLineFromPosition(part.SpanStart);
        var indent = new string(
            text.ToString(TextSpan.FromBounds(first.Start, part.SpanStart))
                .TakeWhile(c => c is ' ' or '\t')
                .ToArray()
        );
        var nested = text
            .Lines.Select(line => new string(
                line.ToString().TakeWhile(c => c is ' ' or '\t').ToArray()
            ))
            .Where(value => value.StartsWith(indent) && value.Length > indent.Length)
            .OrderBy(value => value.Length)
            .FirstOrDefault();
        var unit = nested is null
            ? indent.Contains('\t')
                ? "\t"
                : "    "
            : nested[indent.Length..];
        var lineBreak = text.Lines.FirstOrDefault(line => line.EndIncludingLineBreak > line.End);
        var newline =
            lineBreak.EndIncludingLineBreak > lineBreak.End
                ? text.ToString(TextSpan.FromBounds(lineBreak.End, lineBreak.EndIncludingLineBreak))
                : "\n";
        var last = text.Lines.GetLineFromPosition(part.CloseBraceToken.SpanStart);
        var prefix = text.ToString(TextSpan.FromBounds(last.Start, part.CloseBraceToken.SpanStart));
        var ownLine = prefix.All(c => c is ' ' or '\t');
        var declaration = member.NormalizeWhitespace(unit, newline).ToFullString();
        return (
            ownLine ? last.Start : part.CloseBraceToken.SpanStart,
            (ownLine ? "" : newline)
                + indent
                + unit
                + declaration
                + newline
                + (ownLine ? "" : indent)
        );
    }
}
