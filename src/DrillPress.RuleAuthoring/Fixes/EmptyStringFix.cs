using Microsoft.CodeAnalysis.CSharp;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Fixes;

/// <summary>Proposes a literal only when rewriting the enclosing expression preserves binding and conversions.</summary>
public static class EmptyStringFix
{
    /// <summary>Creates a complete replacement with independent validation for every affected source membership.</summary>
    public static FixProposal? Create(MemberReference reference)
    {
        if (reference.Source is not { Document.IsEditable: true } source || reference.Syntax is not { } expression || ContextualRewrite.HasInteriorContent(expression))
        {
            return null;
        }

        var edit = new SourceEdit(source.Document.FileIdentity, source.Document.Fingerprint, expression.SpanStart,
            expression.Span.Length, expression.ToString(), "\"\"");
        return new FixProposal([edit], project => Validate(project, edit));
    }

    private static bool Validate(AnalysisProject project, SourceEdit edit)
    {
        var sources = project.Sources.Where(source => source.Document.FileIdentity == edit.FileIdentity).ToArray();
        return sources.Length > 0 && sources.All(source => Validate(source, edit));
    }

    private static bool Validate(AnalysisSource source, SourceEdit edit)
    {
        if (!source.Document.IsEditable || source.Document.IsGenerated || source.Document.Fingerprint != edit.Fingerprint)
        {
            return false;
        }

        var span = new TextSpan(edit.Start, edit.Length);
        var expression = source.Tree.GetRoot().FindNode(span, getInnermostNodeForTie: true) as ExpressionSyntax;
        return expression is not null && expression.Span == span && expression.ToString() == edit.OriginalText &&
            source.Model.GetSymbolInfo(expression).Symbol is Microsoft.CodeAnalysis.IFieldSymbol { Name: "Empty" } field &&
            CodeType.Of<string>().Matches(field.ContainingType) && ContextualRewrite.PreservesBinding(source, expression, edit.Replacement);
    }
}
