using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

internal static class SymbolQueries
{
    internal static CodeQuery<CodeSymbol> Declarations { get; } = DeclarationsIn(Sources.Files);

    internal static CodeQuery<CodeSymbol> DeclarationsIn(CodeQuery<CodeFile> files) =>
        files
            .SelectMany(file => file.Nodes<SyntaxNode>())
            .SelectMany(node =>
                node.Source.Model.GetDeclaredSymbol(
                    node.Syntax,
                    node.Source.Project.CancellationToken
                )
                    is { } symbol
                    ? new[] { new CodeSymbol(node.Source, node.Syntax, symbol) }
                    : []
            );

    private static CodeQuery<CodeSymbol> References { get; } =
        Sources
            .Nodes<SimpleNameSyntax>()
            .SelectMany(node =>
                node
                    .Source.Model.GetSymbolInfo(node.Syntax, node.Source.Project.CancellationToken)
                    .Symbol
                    is { } symbol
                    ? new[] { new CodeSymbol(node.Source, node.Syntax, symbol) }
                    : []
            );

    internal static CodeQuery<CodeSymbol> ReferencesTo(ISymbol target) =>
        CodeQuery<CodeSymbol>.Create(solution =>
        {
            var files = solution
                .Projects.SelectMany(project => project.Sources)
                .DistinctBy(source => source.Tree)
                .ToDictionary(source => source.Tree, source => source.Document.FileIdentity);
            var declarations = target
                .OriginalDefinition.DeclaringSyntaxReferences.Where(reference =>
                    files.ContainsKey(reference.SyntaxTree)
                )
                .Select(reference => (File: files[reference.SyntaxTree], reference.Span))
                .ToHashSet();
            return References
                .In(solution)
                .Where(reference =>
                    SymbolEqualityComparer.Default.Equals(
                        reference.Symbol.OriginalDefinition,
                        target.OriginalDefinition
                    )
                    || reference.Symbol.OriginalDefinition.DeclaringSyntaxReferences.Any(
                        declaration =>
                            files.TryGetValue(declaration.SyntaxTree, out var file)
                            && declarations.Contains((file, declaration.Span))
                    )
                );
        });
}
