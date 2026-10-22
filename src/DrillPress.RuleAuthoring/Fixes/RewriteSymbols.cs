using Microsoft.CodeAnalysis;

namespace DrillPress;

internal static class RewriteSymbols
{
    private static readonly SymbolDisplayFormat _format = SymbolDisplayFormat
        .FullyQualifiedFormat.WithMemberOptions(
            SymbolDisplayMemberOptions.IncludeContainingType
                | SymbolDisplayMemberOptions.IncludeParameters
                | SymbolDisplayMemberOptions.IncludeType
                | SymbolDisplayMemberOptions.IncludeRef
        )
        .WithParameterOptions(
            SymbolDisplayParameterOptions.IncludeType
                | SymbolDisplayParameterOptions.IncludeParamsRefOut
        )
        .WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters);

    internal static string? Identity(ISymbol? symbol) =>
        symbol is null
            ? null
            : symbol.Kind
                + ":"
                + symbol.ContainingAssembly?.Identity
                + ":"
                + symbol.ToDisplayString(_format);

    internal static bool Same(ISymbol? before, ISymbol? after, RewriteContext context)
    {
        if (Identity(before) != Identity(after))
            return false;
        if (before is null || after is null)
            return before is null && after is null;
        // Every declaration of a namespace merges into one symbol named by its qualified name, and its
        // declaring references point at name parts (Company in namespace Company.Import), not whole
        // declarations, so the qualified identity is the complete identity.
        if (before is INamespaceSymbol)
            return true;
        foreach (var reference in before.DeclaringSyntaxReferences)
        {
            var source = context.Original.Sources.FirstOrDefault(source =>
                source.Tree == reference.SyntaxTree
            );
            if (source is null)
                continue;
            if (context.TreeFor(source) is not { } tree)
                return false;
            if (
                context.MapSpan(source, reference.Span) is { } span
                && after.DeclaringSyntaxReferences.Any(candidate =>
                    candidate.SyntaxTree == tree && candidate.Span == span
                )
            )
                return true;
            // An edit at or across the declaration's boundary, such as a removed leading modifier, can move
            // its start onto retained trivia: anchor on an unchanged token instead.
            var beforeDeclaration = reference.GetSyntax(context.Original.CancellationToken);
            foreach (var token in beforeDeclaration.DescendantTokens())
                if (context.MapToken(source, token) is { } mapped)
                {
                    var declaration = mapped
                        .Parent?.AncestorsAndSelf()
                        .FirstOrDefault(node => node.RawKind == beforeDeclaration.RawKind);
                    return declaration is not null
                        && after.DeclaringSyntaxReferences.Any(candidate =>
                            candidate.SyntaxTree == tree
                            && candidate.GetSyntax(context.Original.CancellationToken) is { } node
                            && node.Span == declaration.Span
                            && node.RawKind == declaration.RawKind
                        );
                }
            return false;
        }
        return true;
    }
}
