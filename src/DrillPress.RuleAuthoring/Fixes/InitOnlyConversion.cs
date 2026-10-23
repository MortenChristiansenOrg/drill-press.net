using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Converts an unrestricted automatic property setter to init, leaving the caller responsible for external construction and serialization contracts.</summary>
public sealed class InitOnlyConversion
{
    private readonly AnalysisSource _source;
    private readonly SyntaxNode? _declaration;

    internal InitOnlyConversion(AnalysisSource source, SyntaxNode? declaration)
    {
        _source = source;
        _declaration = declaration;
    }

    /// <summary>Proposes the conversion when the property remains the same symbol, becomes init-only, and the caller's policy holds. Restricted or implemented setters are withheld.</summary>
    public FixProposal? SafeWhen(Func<IPropertySymbol, bool> permitsInitOnly)
    {
        if (_declaration is not PropertyDeclarationSyntax property)
            return null;
        var accessor = property.AccessorList?.Accessors.FirstOrDefault(accessor =>
            accessor.IsKind(SyntaxKind.SetAccessorDeclaration)
        );
        if (
            accessor is null
            || accessor.Modifiers.Count > 0
            || accessor.Body is not null
            || accessor.ExpressionBody is not null
        )
            return null;
        var before = _source.Model.GetDeclaredSymbol(property);
        return SourceChanges.Propose(
            [SourceChanges.Replace(_source, accessor.Keyword.Span, "init")],
            context =>
                context.Map(_source, property) is { After: PropertyDeclarationSyntax after } mapped
                && mapped.Model.GetDeclaredSymbol(after)
                    is IPropertySymbol { SetMethod.IsInitOnly: true } symbol
                && context.Same(before, symbol)
                && permitsInitOnly(symbol)
        );
    }
}
