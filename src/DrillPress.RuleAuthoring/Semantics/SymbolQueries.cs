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

    /// <summary>Selects references using compiler identity or a loaded source-file identity and declaration span shared across compilations. Metadata symbols rely on compiler identity.</summary>
    public static CodeQuery<CodeSymbol> ReferencesTo(ISymbol target) => CodeQuery<CodeSymbol>.Create(solution =>
    {
        var files = solution.Projects.SelectMany(project => project.Sources).DistinctBy(source => source.Tree)
            .ToDictionary(source => source.Tree, source => source.Document.FileIdentity);
        var declarations = target.OriginalDefinition.DeclaringSyntaxReferences
            .Where(reference => files.ContainsKey(reference.SyntaxTree))
            .Select(reference => (File: files[reference.SyntaxTree], reference.Span)).ToHashSet();
        return References.In(solution).Where(reference =>
            SymbolEqualityComparer.Default.Equals(reference.Symbol.OriginalDefinition, target.OriginalDefinition) ||
            reference.Symbol.OriginalDefinition.DeclaringSyntaxReferences.Any(declaration =>
                files.TryGetValue(declaration.SyntaxTree, out var file) && declarations.Contains((file, declaration.Span))));
    });
}
