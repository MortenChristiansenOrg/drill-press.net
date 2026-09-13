using DrillPress.Engine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Conformance;

internal static class SemanticSignatures
{
    public static string[] Capture(CompilationContext context, CancellationToken cancellationToken)
    {
        var entries = new List<string>();
        foreach (
            var (tree, document) in context.Compilation.SyntaxTrees.Zip(context.Snapshot.Documents)
        )
        {
            var model = context.Compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot(cancellationToken).DescendantNodes())
            {
                if (node is ExpressionSyntax or AttributeSyntax or ConstructorInitializerSyntax)
                {
                    var info = model.GetSymbolInfo(node, cancellationToken);
                    var type = model.GetTypeInfo(node, cancellationToken);
                    entries.Add(
                        $"{document.DocumentId}:{node.Span}:{node.RawKind}:{Describe(info.Symbol)}:{info.CandidateReason}:{Describe(type.Type)}:{Describe(type.ConvertedType)}"
                    );
                }
                else if (node is MemberDeclarationSyntax)
                {
                    entries.Add(
                        $"{document.DocumentId}:{node.Span}:declaration:{Describe(model.GetDeclaredSymbol(node, cancellationToken))}"
                    );
                }
            }
        }

        var memberships = context
            .Compilation.SyntaxTrees.Zip(context.Snapshot.Documents)
            .ToDictionary(pair => pair.First, pair => pair.Second.DocumentId);
        foreach (var diagnostic in context.Compilation.GetDiagnostics(cancellationToken))
        {
            var location = diagnostic.Location.SourceTree is { } tree
                ? $"{memberships[tree]}:{diagnostic.Location.SourceSpan}"
                : "compilation";
            entries.Add(
                $"diagnostic:{location}:{diagnostic.Id}:{diagnostic.Severity}:{diagnostic.IsSuppressed}:{diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}"
            );
        }

        return entries.Order(StringComparer.Ordinal).ToArray();
    }

    private static string Describe(ISymbol? symbol) =>
        symbol is null
            ? "-"
            : $"{symbol.Kind}:{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
            .WithMemberOptions(SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType)
            .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut)
            .WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters))}@{symbol.ContainingAssembly?.Identity}";
}
