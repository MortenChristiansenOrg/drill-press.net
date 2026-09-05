using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

/// <summary>
/// Reconstructs Roslyn compilations from an exported snapshot and presents semantic
/// member references to a compiled rule set.
/// </summary>
public static class AnalysisEngine
{
    /// <summary>Analyzes one snapshot and returns its deterministically ordered diagnostics.</summary>
    /// <param name="rules">The statically constructed rules to evaluate.</param>
    /// <param name="snapshotPath">The compilation snapshot exported by BuildHost.</param>
    /// <param name="cancellationToken">Stops snapshot loading and analysis.</param>
    public static async Task<IReadOnlyList<RuleDiagnostic>> AnalyzeAsync(
        RuleSet rules,
        string snapshotPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);

        var snapshot = await CompilationSnapshot.ReadAsync(snapshotPath, cancellationToken);
        return await AnalyzeAsync(rules, snapshot, cancellationToken);
    }

    /// <summary>Analyzes an in-memory snapshot and returns its deterministically ordered diagnostics.</summary>
    /// <param name="rules">The statically constructed rules to evaluate.</param>
    /// <param name="snapshot">The compilation snapshot to analyze.</param>
    /// <param name="cancellationToken">Stops analysis.</param>
    public static async Task<IReadOnlyList<RuleDiagnostic>> AnalyzeAsync(
        RuleSet rules,
        CompilationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var references = new Dictionary<string, MetadataReference>(PathComparer);
        var memberReferences = new List<MemberReference>();

        foreach (var project in snapshot.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (compilation, syntaxTrees) = CreateCompilation(
                project,
                references,
                cancellationToken);

            foreach (var (tree, document) in syntaxTrees.Zip(project.Documents))
            {
                if (document.IsGenerated)
                {
                    continue;
                }

                memberReferences.AddRange(await FindMemberReferencesAsync(
                    compilation,
                    tree,
                    document,
                    cancellationToken));
            }
        }

        return rules.Evaluate(memberReferences);
    }

    private static (CSharpCompilation Compilation, SyntaxTree[] SyntaxTrees) CreateCompilation(
        ProjectSnapshot project,
        Dictionary<string, MetadataReference> references,
        CancellationToken cancellationToken)
    {
        var parseOptions = new CSharpParseOptions(
            (LanguageVersion)project.LanguageVersion,
            preprocessorSymbols: project.PreprocessorSymbols);
        var syntaxTrees = project.Documents
            .Select(document => CSharpSyntaxTree.ParseText(
                SourceText.From(document.Text),
                parseOptions,
                document.Path,
                cancellationToken: cancellationToken))
            .ToArray<SyntaxTree>();
        var metadataReferences = project.MetadataReferences
            .Select(path => GetMetadataReference(path, references))
            .Concat(project.ProjectReferences.Select(reference => MetadataReference.CreateFromImage(
                reference.Image,
                MetadataReferenceProperties.Assembly
                    .WithAliases(reference.Aliases)
                    .WithEmbedInteropTypes(reference.EmbedInteropTypes))))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            project.AssemblyName,
            syntaxTrees,
            metadataReferences,
            new CSharpCompilationOptions((OutputKind)project.OutputKind)
                .WithNullableContextOptions((NullableContextOptions)project.NullableContextOptions)
                .WithConcurrentBuild(true));
        return (compilation, syntaxTrees);
    }

    private static MetadataReference GetMetadataReference(
        string path,
        Dictionary<string, MetadataReference> references)
    {
        if (!references.TryGetValue(path, out var reference))
        {
            reference = MetadataReference.CreateFromFile(path);
            references.Add(path, reference);
        }

        return reference;
    }

    private static async Task<IReadOnlyList<MemberReference>> FindMemberReferencesAsync(
        CSharpCompilation compilation,
        SyntaxTree tree,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync(cancellationToken);
        return root.DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .Select(name => CreateMemberReference(
                semanticModel,
                tree,
                document.Path,
                name,
                cancellationToken))
            .Where(reference => reference is not null)
            .Select(reference => reference!)
            .ToArray();
    }

    private static MemberReference? CreateMemberReference(
        SemanticModel semanticModel,
        SyntaxTree tree,
        string documentPath,
        SimpleNameSyntax name,
        CancellationToken cancellationToken)
    {
        var expression = GetCompleteMemberReference(name);
        if (expression is null)
        {
            return null;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(expression, cancellationToken);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (symbol is not (IFieldSymbol or IPropertySymbol or IMethodSymbol) ||
            symbol.ContainingType is null)
        {
            return null;
        }

        var lineSpan = tree.GetLineSpan(expression.Span, cancellationToken).StartLinePosition;
        return new MemberReference(
            CodeType.Named(GetMetadataName(symbol.ContainingType)),
            symbol.Name,
            new SourceLocation(
                documentPath,
                expression.Span.Start,
                expression.Span.Length,
                lineSpan.Line + 1,
                lineSpan.Character + 1));
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

    private static IEqualityComparer<string> PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : EqualityComparer<string>.Default;
}
