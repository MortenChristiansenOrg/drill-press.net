using Microsoft.CodeAnalysis;

namespace DrillPress.Fixes;

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
            if (
                context.Map(source, reference.GetSyntax(context.Original.CancellationToken))
                is not { } declaration
            )
                return false;
            return after.DeclaringSyntaxReferences.Any(candidate =>
                candidate.SyntaxTree == declaration.After.SyntaxTree
                && candidate.Span == declaration.After.Span
            );
        }
        return true;
    }
}
