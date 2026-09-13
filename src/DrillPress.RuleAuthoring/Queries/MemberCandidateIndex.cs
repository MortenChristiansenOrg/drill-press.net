using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.Queries;

internal sealed class MemberCandidateIndex
{
    private readonly MemberSyntaxCandidate[] _candidates;
    private readonly Dictionary<MemberSyntaxCandidate, MemberReference?> _bindings = [];
    private readonly CancellationToken _cancellationToken;

    internal MemberCandidateIndex(
        IEnumerable<AnalysisSource> sources,
        CancellationToken cancellationToken
    )
    {
        _cancellationToken = cancellationToken;
        _candidates = sources.SelectMany(Discover).ToArray();
    }

    internal long Bindings => _bindings.Count;

    internal IEnumerable<MemberReference> Select(IReadOnlySet<string>? names)
    {
        foreach (var candidate in _candidates)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (names is not null && !names.Contains(candidate.Name))
            {
                continue;
            }

            if (!_bindings.TryGetValue(candidate, out var reference))
            {
                reference = Bind(candidate);
                _bindings.Add(candidate, reference);
            }

            if (reference is not null)
            {
                yield return reference;
            }
        }
    }

    private IEnumerable<MemberSyntaxCandidate> Discover(AnalysisSource source)
    {
        foreach (
            var name in source
                .Tree.GetRoot(source.Project.CancellationToken)
                .DescendantNodes()
                .OfType<SimpleNameSyntax>()
        )
        {
            _cancellationToken.ThrowIfCancellationRequested();
            source.Project.CancellationToken.ThrowIfCancellationRequested();
            ExpressionSyntax? expression =
                name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access
                : name is IdentifierNameSyntax
                && name.Parent is not (QualifiedNameSyntax or AliasQualifiedNameSyntax)
                    ? name
                : null;
            if (expression is not null)
            {
                yield return new(source, expression, name.Identifier.ValueText);
            }
        }
    }

    private MemberReference? Bind(MemberSyntaxCandidate candidate)
    {
        candidate.Source.Project.CancellationToken.ThrowIfCancellationRequested();
        var symbol = candidate
            .Source.Model.GetSymbolInfo(
                candidate.Syntax,
                candidate.Source.Project.CancellationToken
            )
            .Symbol;
        if (
            symbol
            is not (
                (IFieldSymbol or IPropertySymbol or IMethodSymbol)
                and { ContainingType: { IsAnonymousType: false } }
            )
        )
        {
            return null;
        }

        return new MemberReference(
            CodeType.FromSymbol(symbol.ContainingType),
            symbol.Name,
            candidate.Source.Locate(candidate.Syntax.Span)
        )
        {
            Source = candidate.Source,
            Syntax = candidate.Syntax,
            Symbol = symbol,
        };
    }
}
