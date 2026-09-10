using DrillPress.Queries;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Semantics;

/// <summary>Lazy declaration and named-reference roots, retaining each ordinary source occurrence and its compilation context.</summary>
public static class SymbolQueries
{
    /// <summary>All resolved declarations, including fields, properties, events, accessors, parameters and locals. Partial declarations remain separate occurrences.</summary>
    public static CodeQuery<CodeSymbol> Declarations { get; } = DeclarationsIn(Sources.Files);

    /// <summary>Discovers declarations only in selected files, avoiding semantic work outside the reporting scope.</summary>
    public static CodeQuery<CodeSymbol> DeclarationsIn(CodeQuery<CodeFile> files) => files.SelectMany(file => file.Nodes<SyntaxNode>()).SelectMany(node =>
        node.Source.Model.GetDeclaredSymbol(node.Syntax, node.Source.Project.CancellationToken) is { } symbol
            ? new[] { new CodeSymbol(node.Source, node.Syntax, symbol) } : []);

    /// <summary>Resolved simple-name references to types, namespaces and members; implicit references are available through operation roots.</summary>
    public static CodeQuery<CodeSymbol> References { get; } = Sources.Nodes<SimpleNameSyntax>().SelectMany(node =>
        node.Source.Model.GetSymbolInfo(node.Syntax, node.Source.Project.CancellationToken).Symbol is { } symbol
            ? new[] { new CodeSymbol(node.Source, node.Syntax, symbol) } : []);

    /// <summary>Selects references to a symbol using compiler identity or a shared source declaration. Metadata symbols rely on compiler identity.</summary>
    public static CodeQuery<CodeSymbol> ReferencesTo(ISymbol target) => References.Where(reference =>
        SymbolEqualityComparer.Default.Equals(reference.Symbol.OriginalDefinition, target.OriginalDefinition) ||
        reference.Symbol.OriginalDefinition.DeclaringSyntaxReferences.Any(left => target.OriginalDefinition.DeclaringSyntaxReferences
            .Any(right => left.SyntaxTree == right.SyntaxTree && left.Span == right.Span)));
}
