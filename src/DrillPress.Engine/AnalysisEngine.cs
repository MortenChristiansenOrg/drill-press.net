using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

public static class AnalysisEngine
{
    public static async Task<IReadOnlyList<RuleDiagnostic>> AnalyzeAsync(
        RuleSet rules,
        string snapshotPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);

        var snapshot = await CompilationSnapshot.ReadAsync(snapshotPath, cancellationToken);
        var references = new Dictionary<string, MetadataReference>(PathComparer);
        var memberReferences = new List<MemberReference>();

        foreach (var project in snapshot.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parseOptions = new CSharpParseOptions(
                (LanguageVersion)project.LanguageVersion,
                preprocessorSymbols: project.PreprocessorSymbols);
            var syntaxTrees = project.Documents
                .Select(document => CSharpSyntaxTree.ParseText(
                    SourceText.From(document.Text),
                    parseOptions,
                    document.Path,
                    cancellationToken: cancellationToken))
                .ToArray();
            var metadataReferences = project.MetadataReferences.Select(path =>
            {
                if (!references.TryGetValue(path, out var reference))
                {
                    reference = MetadataReference.CreateFromFile(path);
                    references.Add(path, reference);
                }

                return reference;
            });
            var compilation = CSharpCompilation.Create(
                project.AssemblyName,
                syntaxTrees,
                metadataReferences,
                new CSharpCompilationOptions((OutputKind)project.OutputKind)
                    .WithNullableContextOptions((NullableContextOptions)project.NullableContextOptions)
                    .WithConcurrentBuild(true));

            foreach (var (tree, document) in syntaxTrees.Zip(project.Documents))
            {
                if (document.IsGenerated)
                {
                    continue;
                }

                var semanticModel = compilation.GetSemanticModel(tree);
                foreach (var name in (await tree.GetRootAsync(cancellationToken))
                             .DescendantNodes()
                             .OfType<SimpleNameSyntax>())
                {
                    var expression = GetCompleteMemberReference(name);
                    if (expression is null)
                    {
                        continue;
                    }

                    var symbolInfo = semanticModel.GetSymbolInfo(expression, cancellationToken);
                    var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
                    if (symbol is not (IFieldSymbol or IPropertySymbol or IMethodSymbol) ||
                        symbol.ContainingType is null)
                    {
                        continue;
                    }

                    var lineSpan = tree.GetLineSpan(expression.Span, cancellationToken).StartLinePosition;
                    memberReferences.Add(new MemberReference(
                        CodeType.Named(GetMetadataName(symbol.ContainingType)),
                        symbol.Name,
                        new SourceLocation(
                            document.Path,
                            expression.Span.Start,
                            expression.Span.Length,
                            lineSpan.Line + 1,
                            lineSpan.Character + 1)));
                }
            }
        }

        return rules.Evaluate(memberReferences);
    }

    private static ExpressionSyntax? GetCompleteMemberReference(SimpleNameSyntax name)
    {
        if (name.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == name)
        {
            return memberAccess;
        }

        return name is IdentifierNameSyntax && name.Parent is not (QualifiedNameSyntax or AliasQualifiedNameSyntax)
            ? name
            : null;
    }

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        if (type.ContainingType is not null)
        {
            return $"{GetMetadataName(type.ContainingType)}+{type.MetadataName}";
        }

        return type.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
            ? $"{containingNamespace.ToDisplayString()}.{type.MetadataName}"
            : type.MetadataName;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
