using System.Globalization;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

internal static class LiteralSyntax
{
    internal static ExpressionSyntax Create(object? value) =>
        value switch
        {
            null => SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression),
            string text => SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(text)
            ),
            char character => SyntaxFactory.LiteralExpression(
                SyntaxKind.CharacterLiteralExpression,
                SyntaxFactory.Literal(character)
            ),
            bool flag => SyntaxFactory.LiteralExpression(
                flag ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression
            ),
            Enum enumeration => SyntaxFactory.CastExpression(
                SyntaxFactory.ParseTypeName(
                    RuntimeName(enumeration.GetType(), enumeration.GetType().GenericTypeArguments)
                ),
                SyntaxFactory.ParenthesizedExpression(
                    Create(
                        Convert.ChangeType(
                            enumeration,
                            Enum.GetUnderlyingType(enumeration.GetType()),
                            CultureInfo.InvariantCulture
                        )
                    )
                )
            ),
            byte or sbyte or short or ushort => SyntaxFactory.ParseExpression(
                $"({SmallType(value)}){Convert.ToString(value, CultureInfo.InvariantCulture)}"
            ),
            int number => Parse(number, ""),
            uint number => Parse(number, "U"),
            long number => Parse(number, "L"),
            ulong number => Parse(number, "UL"),
            float number when !float.IsFinite(number) => SpecialFloat("Single", number),
            double number when !double.IsFinite(number) => SpecialFloat("Double", number),
            float number => SyntaxFactory.ParseExpression(
                number.ToString("R", CultureInfo.InvariantCulture) + "F"
            ),
            double number => SyntaxFactory.ParseExpression(
                number.ToString("R", CultureInfo.InvariantCulture) + "D"
            ),
            decimal number => Parse(number, "M"),
            _ => throw new ArgumentException(
                "A literal requires a C# primitive, enum value or null.",
                nameof(value)
            ),
        };

    private static string SmallType(object value) =>
        value switch
        {
            byte => "byte",
            sbyte => "sbyte",
            short => "short",
            _ => "ushort",
        };

    private static ExpressionSyntax Parse(IFormattable value, string suffix) =>
        SyntaxFactory.ParseExpression(value.ToString(null, CultureInfo.InvariantCulture) + suffix);

    private static ExpressionSyntax SpecialFloat(string type, double value) =>
        SyntaxFactory.ParseExpression(
            "global::System."
                + type
                + "."
                + (
                    double.IsNaN(value) ? "NaN"
                    : value > 0 ? "PositiveInfinity"
                    : "NegativeInfinity"
                )
        );

    private static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

    private static string RuntimeName(Type type, Type[] arguments)
    {
        var parentCount = type.DeclaringType?.GetGenericArguments().Length ?? 0;
        var prefix = type.DeclaringType is { } parent
            ? RuntimeName(parent, arguments.Take(parentCount).ToArray()) + "."
            : "global::"
                + (
                    string.IsNullOrEmpty(type.Namespace)
                        ? ""
                        : string.Join(".", type.Namespace.Split('.').Select(Identifier)) + "."
                );
        var name = Identifier(type.Name.Split('`')[0]);
        var own = arguments.Skip(parentCount).ToArray();
        return prefix
            + name
            + (
                own.Length == 0
                    ? ""
                    : "<"
                        + string.Join(
                            ",",
                            own.Select(argument =>
                                RuntimeName(argument, argument.GenericTypeArguments)
                            )
                        )
                        + ">"
            );
    }
}
