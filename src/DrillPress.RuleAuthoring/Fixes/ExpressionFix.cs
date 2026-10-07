using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A selected expression ready for replacement. Replacements always need a behavior proof with <see cref="ExpressionReplacement.SafeWhen"/>.</summary>
public class ExpressionFix
{
    internal ExpressionFix(AnalysisSource source, ExpressionSyntax target)
    {
        Source = source;
        Target = target;
    }

    internal AnalysisSource Source { get; }

    internal ExpressionSyntax Target { get; }

    /// <summary>Replaces the expression with a C# expression template whose numbered holes embed original sub-expressions unchanged, such as <c>ReplaceWith("{0} == {1}", left, right)</c>.</summary>
    /// <param name="template">A complete C# expression; <c>{0}</c>, <c>{1}</c> and so on refer to <paramref name="keep"/>. Parentheses are added only where precedence requires them.</param>
    /// <param name="keep">Sub-expressions of the replaced expression to keep. Their bindings are verified, and repeated or unused holes stay visible to evaluation checks.</param>
    public ExpressionReplacement ReplaceWith(string template, params CodeExpression[] keep)
    {
        var inputs = keep.Select(expression => new ExpressionInput(
                expression.Source,
                expression.Syntax
            ))
            .ToArray();
        var built = ExpressionTemplates.Create(template, inputs);
        return new(Source, Target, built.Syntax, inputs);
    }

    /// <summary>Replaces the expression with a typed literal: a string, character, Boolean, number, enum value or null.</summary>
    public ExpressionReplacement ReplaceWithLiteral(object? value) =>
        new(Source, Target, LiteralSyntax.Create(value), []);

    /// <summary>Replaces the expression with <c>left == right</c>, keeping both operands. With <paramref name="absorbNegation"/>, an enclosing <c>!</c> becomes <c>left != right</c>. This construction does not prove that the two forms are equivalent.</summary>
    public ExpressionReplacement ReplaceWithEquality(
        CodeExpression left,
        CodeExpression right,
        bool absorbNegation = false
    ) => ExpressionReplacement.Equality(Source, Target, left, right, absorbNegation);

    /// <summary>Replaces the expression with hand-built syntax. Prefer the template overload, which keeps original operands verifiable.</summary>
    public ExpressionReplacement ReplaceWith(ExpressionSyntax replacement) =>
        new(Source, Target, replacement, []);
}
