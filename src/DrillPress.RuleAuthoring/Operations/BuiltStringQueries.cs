using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>String source inspection and explicit constructor argument projection, independent of URL policy.</summary>
public static class BuiltStringQueries
{
    /// <summary>Flattens built-in string literals, interpolations and concatenations. Constant references remain holes unless expansion is explicitly requested. Unsupported string expressions remain one opaque hole.</summary>
    public static BuiltString? AsBuiltString(
        this CodeExpression expression,
        bool expandConstants = false
    ) => expression.AsBuiltString(expandConstants, stringOperandsOnly: false);

    /// <summary>Flattens string source with explicit constant and concatenation policies. Set stringOperandsOnly to reject top-level concatenations that implicitly format non-string operands; interpolation holes are unaffected.</summary>
    public static BuiltString? AsBuiltString(
        this CodeExpression expression,
        bool expandConstants,
        bool stringOperandsOnly
    )
    {
        if (!expression.IsResolved || expression.Type?.SpecialType != SpecialType.System_String)
            return null;
        if (stringOperandsOnly && HasNonStringConcatenation(expression))
            return null;
        var parts = new List<BuiltStringPart>();
        Read(expression, expandConstants, parts);
        return new(parts.AsReadOnly());
    }

    // Interpolation holes are their own formatting boundary, so only inspect the top-level
    // concatenation tree. A non-string concatenation operand can invoke implicit formatting.
    private static bool HasNonStringConcatenation(CodeExpression expression)
    {
        if (expression.Constant is { HasValue: true, Value: string })
            return false;
        if (expression.Syntax is ParenthesizedExpressionSyntax parentheses)
            return HasNonStringConcatenation(new(expression.Source, parentheses.Expression));
        if (
            expression.Syntax is not BinaryExpressionSyntax binary
            || !binary.IsKind(SyntaxKind.AddExpression)
        )
            return false;
        var left = new CodeExpression(expression.Source, binary.Left);
        var right = new CodeExpression(expression.Source, binary.Right);
        return left.Type?.SpecialType != SpecialType.System_String
            || right.Type?.SpecialType != SpecialType.System_String
            || HasNonStringConcatenation(left)
            || HasNonStringConcatenation(right);
    }

    /// <summary>Projects the explicit source value passed to a bound constructor parameter, including target-typed new. Defaults or expanded multi-value params are not invented as single expressions.</summary>
    public static CodeExpression? ConstructorArgument(
        this CodeExpression expression,
        string parameter
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        if (!expression.IsResolved || expression.Operation is not IObjectCreationOperation creation)
            return null;
        var arguments = creation
            .Arguments.Where(argument => argument.Parameter?.Name == parameter)
            .ToArray();
        return arguments is [{ IsImplicit: false, Syntax: ArgumentSyntax syntax }]
            ? new(expression.Source, syntax.Expression)
            : null;
    }

    private static void Read(CodeExpression expression, bool expand, List<BuiltStringPart> parts)
    {
        expression.Source.Project.CancellationToken.ThrowIfCancellationRequested();
        if (expression.Syntax is ParenthesizedExpressionSyntax parentheses)
        {
            Read(new(expression.Source, parentheses.Expression), expand, parts);
            return;
        }
        if (
            expression.Syntax is LiteralExpressionSyntax
            && expression.Constant is { HasValue: true, Value: string literal }
        )
        {
            parts.Add(new(BuiltStringPartKind.Literal, literal, null, expression.Location));
            return;
        }
        if (expression.Syntax is InterpolatedStringExpressionSyntax interpolation)
        {
            foreach (var content in interpolation.Contents)
                if (content is InterpolatedStringTextSyntax text)
                    parts.Add(
                        new(
                            BuiltStringPartKind.Literal,
                            text.TextToken.ValueText,
                            null,
                            expression.Source.Locate(text.Span)
                        )
                    );
                else if (content is InterpolationSyntax hole)
                {
                    if (
                        expand
                        && hole.AlignmentClause is null
                        && hole.FormatClause is null
                        && new CodeExpression(expression.Source, hole.Expression).Constant
                            is { HasValue: true, Value: string constantHole }
                    )
                    {
                        parts.Add(
                            new(
                                BuiltStringPartKind.Literal,
                                constantHole,
                                null,
                                expression.Source.Locate(hole.Span)
                            )
                        );
                        continue;
                    }
                    parts.Add(
                        new(
                            BuiltStringPartKind.Hole,
                            null,
                            new(expression.Source, hole.Expression),
                            expression.Source.Locate(hole.Span),
                            hole.AlignmentClause is { } alignment
                                ? new(expression.Source, alignment.Value)
                                : null,
                            hole.FormatClause?.FormatStringToken.ValueText
                        )
                    );
                }
            return;
        }
        if (
            expression.Syntax is BinaryExpressionSyntax binary
            && binary.IsKind(SyntaxKind.AddExpression)
            && expression.Operation
                is IBinaryOperation
                {
                    OperatorKind: BinaryOperatorKind.Add,
                    OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_String
                }
        )
        {
            Read(new(expression.Source, binary.Left), expand, parts);
            Read(new(expression.Source, binary.Right), expand, parts);
            return;
        }
        if (expand && expression.Constant is { HasValue: true, Value: string constant })
            parts.Add(new(BuiltStringPartKind.Literal, constant, null, expression.Location));
        else
            parts.Add(new(BuiltStringPartKind.Hole, null, expression, expression.Location));
    }
}
