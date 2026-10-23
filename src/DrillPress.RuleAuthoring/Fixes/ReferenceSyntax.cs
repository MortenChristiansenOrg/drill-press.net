using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>The shortest spelling of a symbol reference that binds to it at a position.</summary>
/// <remarks>Roslyn's simplifier needs a workspace, which rule bundles do not load. Candidates go from the bare name through minimally qualified forms to a <c>global::</c> name, and the first that binds speculatively to the symbol wins.</remarks>
internal static class ReferenceSyntax
{
    internal static string? Shortest(AnalysisSource source, int position, ISymbol symbol)
    {
        foreach (var candidate in Candidates(source.Model, position, symbol).Distinct())
        {
            var expression = SyntaxFactory.ParseExpression(candidate);
            if (
                !expression.ContainsDiagnostics
                && SymbolEqualityComparer.Default.Equals(
                    Bound(source.Model, position, expression)?.OriginalDefinition,
                    symbol.OriginalDefinition
                )
            )
                return candidate;
        }
        return null;
    }

    private static ISymbol? Bound(SemanticModel model, int position, ExpressionSyntax expression)
    {
        var info = model.GetSpeculativeSymbolInfo(
            position,
            expression,
            SpeculativeBindingOption.BindAsExpression
        );
        return info.Symbol
            ?? model
                .GetSpeculativeSymbolInfo(
                    position,
                    expression,
                    SpeculativeBindingOption.BindAsTypeOrNamespace
                )
                .Symbol;
    }

    private static IEnumerable<string> Candidates(SemanticModel model, int position, ISymbol symbol)
    {
        var name = Escape(symbol.Name);
        if (symbol is ITypeSymbol or INamespaceSymbol)
        {
            yield return symbol.ToMinimalDisplayString(model, position);
            yield return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            yield break;
        }
        yield return name;
        if (symbol.ContainingType is { } type)
        {
            yield return type.ToMinimalDisplayString(model, position) + "." + name;
            yield return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                + "."
                + name;
        }
    }

    private static string Escape(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
