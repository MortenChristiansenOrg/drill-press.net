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
        var result = syntax.ReplaceNodes(
            holes,
            (node, _) =>
                SyntaxFactory.ParenthesizedExpression(
                    inputs[
                        int.Parse(
                            node.Identifier.ValueText[prefix.Length..],
                            CultureInfo.InvariantCulture
                        )
                    ].Syntax
                )
        );
        return new(result, Array.AsReadOnly(inputs.ToArray()));
    }
}
