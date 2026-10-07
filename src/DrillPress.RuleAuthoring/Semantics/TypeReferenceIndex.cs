using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

internal sealed class TypeReferenceIndex
{
    private readonly TypeSyntaxCandidate[] _candidates;
    private readonly HashSet<string> _aliases = [];
    private readonly Dictionary<TypeSyntaxCandidate, CodeTypeReference?> _bindings = [];
    private readonly CancellationToken _cancellationToken;

    internal TypeReferenceIndex(
        IEnumerable<AnalysisSource> sources,
        CancellationToken cancellationToken
    )
    {
        _cancellationToken = cancellationToken;
        _candidates = sources.SelectMany(Discover).ToArray();
    }

    internal IEnumerable<CodeTypeReference> Select(IReadOnlySet<string>? names)
    {
        foreach (var candidate in _candidates)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (
                names is not null
                && !candidate.Keys.Any(names.Contains)
                && !_aliases.Contains(candidate.Keys[0])
            )
                continue;
            if (!_bindings.TryGetValue(candidate, out var reference))
            {
                reference = Bind(candidate);
                _bindings.Add(candidate, reference);
            }
            if (reference is not null)
                yield return reference;
        }
    }

    internal static IEnumerable<string> NamesOf(CodeType type)
    {
        var name = type.MetadataName;
        var start = Math.Max(name.LastIndexOf('.'), name.LastIndexOf('+')) + 1;
        var simple = name[start..];
        var arity = simple.IndexOf('`');
        if (arity >= 0)
            simple = simple[..arity];
        var array = simple.IndexOf('[');
        if (array >= 0)
            simple = simple[..array];
        yield return simple;
        if (
            simple.Length > "Attribute".Length
            && simple.EndsWith("Attribute", StringComparison.Ordinal)
        )
            yield return simple[..^"Attribute".Length];
    }

    // Generated sources, such as SDK global usings, contribute aliases but never candidates.
    private IEnumerable<TypeSyntaxCandidate> Discover(AnalysisSource source)
    {
        var root = source.Tree.GetRoot(source.Project.CancellationToken);
        if (source.Document.IsGenerated)
        {
            foreach (
                var directive in root.DescendantNodes(node =>
                        node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax
                    )
                    .OfType<UsingDirectiveSyntax>()
            )
                AddAlias(directive);
            yield break;
        }
        foreach (var node in root.DescendantNodes())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (node is UsingDirectiveSyntax directive)
                AddAlias(directive);
            if (node is PredefinedTypeSyntax predefined && Keyword(predefined) is { } keyword)
                yield return new(source, predefined, predefined, [keyword]);
            else if (
                node is SimpleNameSyntax name
                && !(name.Parent is AliasQualifiedNameSyntax qualified && qualified.Alias == name)
            )
                yield return new(source, name, Outermost(name), Keys(name.Identifier.ValueText));
        }
    }

    private void AddAlias(UsingDirectiveSyntax directive)
    {
        if (directive.Alias?.Name.Identifier.ValueText is { } alias)
            _aliases.Add(alias);
    }

    // Native-integer keywords are identifiers that bind to IntPtr and UIntPtr; keep the written name for same-named types.
    private static string[] Keys(string identifier) =>
        identifier switch
        {
            "nint" => [identifier, "IntPtr"],
            "nuint" => [identifier, "UIntPtr"],
            _ => [identifier],
        };

    private static ExpressionSyntax Outermost(SimpleNameSyntax name)
    {
        ExpressionSyntax node = name;
        while (true)
        {
            switch (node.Parent)
            {
                case QualifiedNameSyntax qualified when qualified.Right == node:
                case AliasQualifiedNameSyntax alias when alias.Name == node:
                case MemberAccessExpressionSyntax access when access.Name == node:
                    node = (ExpressionSyntax)node.Parent;
                    continue;
                default:
                    return node;
            }
        }
    }

    private CodeTypeReference? Bind(TypeSyntaxCandidate candidate)
    {
        var model = candidate.Source.Model;
        var token = candidate.Source.Project.CancellationToken;
        token.ThrowIfCancellationRequested();
        if (
            candidate.Syntax is IdentifierNameSyntax { IsVar: true } implicitType
            && model.GetAliasInfo(implicitType, token) is null
            && model.GetSymbolInfo(implicitType, token).Symbol
                is not INamedTypeSymbol { Name: "var" }
        )
            return null;
        var type = model.GetSymbolInfo(candidate.Syntax, token).Symbol switch
        {
            INamedTypeSymbol named => named,
            IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                when candidate.Reference.Parent is AttributeSyntax => constructor.ContainingType,
            _ => null,
        };
        return type is null || type.TypeKind == TypeKind.Error
            ? null
            : new(candidate.Source, candidate.Reference, type);
    }

    private static string? Keyword(PredefinedTypeSyntax syntax) =>
        syntax.Keyword.Kind() switch
        {
            SyntaxKind.StringKeyword => "String",
            SyntaxKind.ObjectKeyword => "Object",
            SyntaxKind.BoolKeyword => "Boolean",
            SyntaxKind.CharKeyword => "Char",
            SyntaxKind.ByteKeyword => "Byte",
            SyntaxKind.SByteKeyword => "SByte",
            SyntaxKind.ShortKeyword => "Int16",
            SyntaxKind.UShortKeyword => "UInt16",
            SyntaxKind.IntKeyword => "Int32",
            SyntaxKind.UIntKeyword => "UInt32",
            SyntaxKind.LongKeyword => "Int64",
            SyntaxKind.ULongKeyword => "UInt64",
            SyntaxKind.FloatKeyword => "Single",
            SyntaxKind.DoubleKeyword => "Double",
            SyntaxKind.DecimalKeyword => "Decimal",
            SyntaxKind.VoidKeyword => "Void",
            _ => null,
        };

    private sealed record TypeSyntaxCandidate(
        AnalysisSource Source,
        ExpressionSyntax Syntax,
        ExpressionSyntax Reference,
        IReadOnlyList<string> Keys
    );
}
