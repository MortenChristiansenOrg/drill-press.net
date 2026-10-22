using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>The executable bodies that contain a set of edits, when every edit lies inside one.</summary>
/// <remarks>
/// Binding depends on the declarations of a compilation and on the body being bound. An edit strictly
/// inside a member body cannot add, remove or change a declaration as long as that body is still the
/// body of the same member afterwards, so expressions outside the edited bodies, including every other
/// file, bind exactly as before. Field, constant, enum, parameter-default and attribute initializers are
/// declarations in this sense, and top-level statements can change the synthesized entry point: an edit
/// there has no region, and the caller checks the whole compilation.
/// </remarks>
internal static class EditRegions
{
    internal static IReadOnlyList<(AnalysisSource Source, SyntaxNode Region)>? Read(
        RewriteContext context,
        IReadOnlyList<SourceEdit> edits
    )
    {
        var result = new List<(AnalysisSource, SyntaxNode)>();
        foreach (var file in edits.GroupBy(edit => edit.FileIdentity))
        foreach (
            var source in context.Original.Sources.Where(source =>
                source.Document.FileIdentity == file.Key
            )
        )
        {
            var root = source.Tree.GetRoot(context.Original.CancellationToken);
            foreach (var edit in file)
            {
                var region = Region(root, edit);
                if (region is null || !Retained(context, source, region))
                    return null;
                if (!result.Contains((source, region)))
                    result.Add((source, region));
            }
        }
        return result;
    }

    private static SyntaxNode? Region(SyntaxNode root, SourceEdit edit)
    {
        var end = edit.Start + edit.Length;
        if (edit.Start <= 0 || end >= root.FullSpan.End)
            return null;
        foreach (var node in root.FindToken(edit.Start).Parent?.AncestorsAndSelf() ?? [])
        {
            if (IsMemberBody(node) && node.SpanStart < edit.Start && end < node.Span.End)
                return node;
            if (node is MemberDeclarationSyntax)
                return null;
        }
        return null;
    }

    private static bool IsMemberBody(SyntaxNode node) =>
        node.Parent switch
        {
            BaseMethodDeclarationSyntax member => member.Body == node
                || member.ExpressionBody == node,
            AccessorDeclarationSyntax accessor => accessor.Body == node
                || accessor.ExpressionBody == node,
            BasePropertyDeclarationSyntax property => node is ArrowExpressionClauseSyntax
                && property switch
                {
                    PropertyDeclarationSyntax declaration => declaration.ExpressionBody == node,
                    IndexerDeclarationSyntax declaration => declaration.ExpressionBody == node,
                    _ => false,
                },
            _ => false,
        };

    // The region must still be the body of the same member.
    private static bool Retained(
        RewriteContext context,
        AnalysisSource source,
        SyntaxNode region
    ) =>
        context.Map(source, region) is { } body
        && context.Map(source, region.Parent!) is { } member
        && body.After.RawKind == region.RawKind
        && member.After.RawKind == region.Parent!.RawKind
        && body.After.Parent?.Span == member.After.Span;
}
