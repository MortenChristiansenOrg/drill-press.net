using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Semantics;

/// <summary>Resolves explicit constant/name-of keys against a selected model root. Naming, grammar and visibility remain configurable.</summary>
public sealed class MemberKeyResolver
{
    private readonly Func<string, IReadOnlyList<MemberKeySegment>?> _parse;
    private readonly Func<INamedTypeSymbol, string, IEnumerable<ISymbol>> _members;
    private readonly Func<ISymbol, bool> _visible;

    /// <summary>Creates a resolver. Defaults accept a single literal member name and public readable instance properties/fields; dotted/indexed grammar is opt-in.</summary>
    public MemberKeyResolver(
        Func<string, IReadOnlyList<MemberKeySegment>?>? parse = null,
        Func<INamedTypeSymbol, string, IEnumerable<ISymbol>>? members = null,
        Func<ISymbol, bool>? visible = null
    )
    {
        _parse = parse ?? (key => string.IsNullOrWhiteSpace(key) ? null : [new(key, [])]);
        _members = members ?? Members;
        _visible = visible ?? PublicReadable;
    }

    /// <summary>Resolves a bound nameof chain or configured constant key. Missing, ambiguous, unsupported and wrong-root evidence returns null.</summary>
    public BoundMemberPath? Resolve(CodeExpression key, CodeExpression root)
    {
        if (
            key.Source.Project != root.Source.Project
            || !key.IsResolved
            || !root.IsResolved
            || BoundMemberPath.FromExpression(root) is not { } rootPath
        )
            return null;
        if (key.Operation is INameOfOperation { Argument.Syntax: ExpressionSyntax named })
        {
            var path = BoundMemberPath.FromExpression(new(key.Source, named));
            return path is not null && PrefixMatches(rootPath, path) ? path : null;
        }
        if (
            key.Constant is not { HasValue: true, Value: string text }
            || _parse(text) is not { Count: > 0 } segments
        )
            return null;
        var steps = rootPath.Steps.ToList();
        var type = root.Type;
        foreach (var segment in segments)
        {
            root.Source.Project.CancellationToken.ThrowIfCancellationRequested();
            var step = ResolveStep(type, segment, root.Source.Project.Compilation);
            if (step is null)
                return null;
            steps.Add(step);
            type = step.Type;
        }
        return new(rootPath.Root, steps.AsReadOnly());
    }

    private MemberPathStep? ResolveStep(
        ITypeSymbol? type,
        MemberKeySegment segment,
        Compilation compilation
    )
    {
        if (type is null || type.TypeKind is TypeKind.Error or TypeKind.Dynamic)
            return null;
        if (
            segment.Name is { Length: > 0 } name
            && segment.Indices.Count == 0
            && type is INamedTypeSymbol named
        )
        {
            var members = _members(named, name)
                .Where(symbol =>
                    !symbol.IsStatic && _visible(symbol) && BelongsTo(named, symbol.ContainingType)
                )
                .Distinct(SymbolEqualityComparer.Default)
                .ToArray();
            return members.Length == 1
                ? members[0] switch
                {
                    IPropertySymbol { IsIndexer: false, GetMethod: not null } property => new(
                        property,
                        [],
                        property.Type
                    ),
                    IFieldSymbol field => new(field, [], field.Type),
                    _ => null,
                }
                : null;
        }
        if (
            segment.Name is not null
            || segment.Indices.Count == 0
            || segment.Indices.Any(index => !ValidConstant(index))
        )
            return null;
        var indices = segment
            .Indices.Select(index => new PathConstant(
                compilation.GetSpecialType(index.Type),
                index.Value
            ))
            .ToArray();
        if (
            type is IArrayTypeSymbol array
            && array.Rank == indices.Length
            && indices.All(index => index.Type.SpecialType == SpecialType.System_Int32)
        )
            return new(null, Array.AsReadOnly(indices), array.ElementType);
        if (type is not INamedTypeSymbol owner)
            return null;
        var indexers = Hierarchy(owner)
            .SelectMany(current => current.GetMembers().OfType<IPropertySymbol>())
            .Where(property =>
                property.IsIndexer
                && !property.IsStatic
                && property.GetMethod is not null
                && _visible(property)
                && property.Parameters.Length == indices.Length
                && property
                    .Parameters.Select(
                        (parameter, index) =>
                            SymbolEqualityComparer.Default.Equals(
                                parameter.Type,
                                indices[index].Type
                            )
                    )
                    .All(equal => equal)
            )
            .ToArray();
        return indexers.Length == 1
            ? new(indexers[0], Array.AsReadOnly(indices), indexers[0].Type)
            : null;
    }

    private static bool PrefixMatches(BoundMemberPath prefix, BoundMemberPath path) =>
        prefix.Steps.Count <= path.Steps.Count
        && prefix.CompareTo(
            new BoundMemberPath(path.Root, path.Steps.Take(prefix.Steps.Count).ToArray())
        ) == PathCorrelation.Match;

    private static bool ValidConstant(MemberKeyIndex index) =>
        index switch
        {
            { Type: SpecialType.System_Int32, Value: int } => true,
            { Type: SpecialType.System_String, Value: string or null } => true,
            _ => false,
        };

    private static bool PublicReadable(ISymbol symbol) =>
        symbol.DeclaredAccessibility == Accessibility.Public
        && (
            symbol is not IPropertySymbol property
            || property.GetMethod?.DeclaredAccessibility == Accessibility.Public
        );

    private static bool BelongsTo(INamedTypeSymbol type, INamedTypeSymbol? owner) =>
        Hierarchy(type).Any(current => SymbolEqualityComparer.Default.Equals(current, owner));

    private static IEnumerable<ISymbol> Members(INamedTypeSymbol type, string name)
    {
        if (type.TypeKind == TypeKind.Interface)
            return Hierarchy(type).SelectMany(current => current.GetMembers(name));
        foreach (var current in Hierarchy(type))
        {
            var members = current.GetMembers(name);
            if (members.Length > 0)
                return members;
        }
        return [];
    }

    private static IEnumerable<INamedTypeSymbol> Hierarchy(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            yield return current;
        if (type.TypeKind == TypeKind.Interface)
            foreach (var contract in type.AllInterfaces)
                yield return contract;
    }
}
