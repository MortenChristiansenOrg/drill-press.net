using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

internal sealed class MemberCandidateIndex
{
    private readonly MemberSyntaxCandidate[] _candidates;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        MemberSyntaxCandidate,
        Lazy<MemberReference?>
    > _bindings = [];
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

    internal IEnumerable<MemberReference> Select(
        NameFilter? names,
        Func<AnalysisSource, bool>? sources = null
    )
    {
        foreach (var candidate in _candidates)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (
                names is not null && !CanMatch(candidate, names)
                || sources is not null && !sources(candidate.Source)
            )
            {
                continue;
            }

            var reference = _bindings
                .GetOrAdd(candidate, candidate => new(() => Bind(candidate)))
                .Value;

            if (reference is not null)
            {
                yield return reference;
            }
        }
    }

    // An attribute name binds to the attribute's constructor, whose compiler name is .ctor.
    private static bool CanMatch(MemberSyntaxCandidate candidate, NameFilter names) =>
        names.Accepts(candidate.Name)
        || candidate.Syntax.Parent is AttributeSyntax
            && names.Accepts(WellKnownMemberNames.InstanceConstructorName);

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
        using var scope = candidate.Source.Project.MeasureShared("member-references");
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

        return new MemberReference(candidate.Source, candidate.Syntax, symbol);
    }
}
