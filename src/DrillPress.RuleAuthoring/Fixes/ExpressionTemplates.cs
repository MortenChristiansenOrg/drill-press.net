using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

internal static class ExpressionTemplates
{
    internal static ExpressionTemplate Create(string template, ExpressionInput[] inputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        var prefix = "__drill_input_";
        while (template.Contains(prefix, StringComparison.Ordinal))
            prefix += "_";
        var occurrences = new List<int>();
        var text = Regex.Replace(
            template,
            @"\{(\d+)\}",
            match =>
            {
                if (
                    !int.TryParse(
                        match.Groups[1].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var index
                    )
                    || index >= inputs.Length
                )
                    throw new ArgumentException(
                        "Each template hole must name a supplied input.",
                        nameof(template)
                    );
                occurrences.Add(index);
                return prefix + index.ToString(CultureInfo.InvariantCulture);
            }
        );
        var syntax = SyntaxFactory.ParseExpression(text);
        if (syntax.ContainsDiagnostics)
            throw new ArgumentException(
                "The template must be a complete C# expression.",
                nameof(template)
            );
        var holes = syntax
            .DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Where(node => node.Identifier.ValueText.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();
        if (holes.Length != occurrences.Count)
            throw new ArgumentException(
                "Template holes must be expression positions, not string or comment contents.",
                nameof(template)
            );
        var parentheses = new List<SyntaxAnnotation>();
        var result = syntax.ReplaceNodes(
            holes,
            (node, _) =>
            {
                var annotation = new SyntaxAnnotation();
                parentheses.Add(annotation);
                return SyntaxFactory
                    .ParenthesizedExpression(
                        inputs[
                            int.Parse(
                                node.Identifier.ValueText[prefix.Length..],
                                CultureInfo.InvariantCulture
                            )
                        ].Syntax
                    )
                    .WithAdditionalAnnotations(annotation);
            }
        );
        return new(
            RemoveRedundantParentheses(result, parentheses),
            Array.AsReadOnly(inputs.ToArray())
        );
    }

    private static ExpressionSyntax RemoveRedundantParentheses(
        ExpressionSyntax result,
        IEnumerable<SyntaxAnnotation> parentheses
    )
    {
        foreach (var annotation in parentheses)
        {
            var wrapper = (ParenthesizedExpressionSyntax)
                result.GetAnnotatedNodes(annotation).Single();
            var candidate = result.ReplaceNode(wrapper, wrapper.Expression.WithTriviaFrom(wrapper));
            var parsed = SyntaxFactory.ParseExpression(
                candidate.NormalizeWhitespace().ToFullString()
            );
            // Ask the parser about precedence, associativity and grammar ambiguities while
            // retaining the original annotated inputs used by contextual rewrite proofs.
            if (!parsed.ContainsDiagnostics && SyntaxFactory.AreEquivalent(candidate, parsed))
                result = candidate;
        }
        return result;
    }
}
