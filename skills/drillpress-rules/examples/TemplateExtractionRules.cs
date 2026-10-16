using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyRules;

/// <summary>A bounded extraction policy for ordinary interpolation of one string parameter.</summary>
/// <remarks>Use only where string contents are the contract: allocation identity/count, resource exhaustion and stack inspection are not observed. No configured calls move into the helper.</remarks>
public static class TemplateExtractionRules
{
    public static RuleCatalog Create()
    {
        var rules = new RuleCatalog();
        rules
            .Rule("TEMPLATE", "Extract the repeated string template.")
            .For(
                Code.Nodes<InterpolatedStringExpressionSyntax>()
                    .Expressions()
                    .TemplateGroups(
                        new(TemplateShapes.Interpolation, capture => capture.TypeIs<string>())
                    )
            )
            .Forbid(fix: group =>
                Fix.Extract(group).ToMethod("FormatValue").SafeWhen(ProvesStringParameterTemplate)
            );
        return rules;
    }

    // The builder proves template/capture correspondence, bindings and caller information.
    // This policy proves the remaining value/formatting and evaluation obligations.
    private static bool ProvesStringParameterTemplate(ExtractionChange change) =>
        change.Occurrences.Count > 0
        && change.Occurrences.All(occurrence =>
            occurrence.Before.TypeIs<string>()
            && occurrence.Before.ConvertedTypeIs(CodeType.Of<string>())
            && occurrence.After.TypeIs<string>()
            && occurrence.Kept is [var capture]
            && capture.Before.TypeIs<string>()
            && capture.Before.Symbol is IParameterSymbol
            && capture.Before.Syntax is IdentifierNameSyntax
            && capture.After is [var argument]
            && argument.TypeIs<string>()
            && occurrence.Before.Syntax is InterpolatedStringExpressionSyntax interpolation
            && interpolation.StringStartToken.IsKind(SyntaxKind.InterpolatedStringStartToken)
            && interpolation.Contents.OfType<InterpolationSyntax>().ToArray() is [var hole]
            && hole.AlignmentClause is null
            && hole.FormatClause is null
            && hole.Expression == capture.Before.Syntax
        );
}
