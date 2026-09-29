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
        foreach (var reference in before.DeclaringSyntaxReferences)
        {
            var source = context.Original.Sources.FirstOrDefault(source =>
                source.Tree == reference.SyntaxTree
            );
            if (source is null)
                continue;
            var beforeDeclaration = reference.GetSyntax(context.Original.CancellationToken);
            var declaration = context.Map(source, beforeDeclaration)?.After;
            if (declaration is null)
                foreach (var token in beforeDeclaration.DescendantTokens())
                    if (context.MapToken(source, token) is { } mapped)
                    {
                        declaration = mapped
                            .Parent?.AncestorsAndSelf()
                            .FirstOrDefault(node => node.RawKind == beforeDeclaration.RawKind);
                        break;
                    }
            if (declaration is null)
                return false;
            return after.DeclaringSyntaxReferences.Any(candidate =>
                candidate.SyntaxTree == declaration.SyntaxTree && candidate.Span == declaration.Span
            );
        }
        return true;
    }
}
