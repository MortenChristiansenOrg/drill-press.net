using Microsoft.CodeAnalysis;

namespace DrillPress.Queries;

/// <summary>An immutable static graph policy. Output filtering is independent from explicit traversal pruning.</summary>
public sealed class TypeTraversal
{
    private readonly Func<ITypeSymbol, IEnumerable<TypeEdge>>[] _edges;
    private readonly Func<ITypeSymbol, bool>[] _hidden;
    private readonly Func<ITypeSymbol, bool> _stop;
    private readonly (CodeType Wrapper, int[] Arguments)[] _opaque;

    /// <summary>Creates a finite traversal with no implicit wrapper/property edges.</summary>
    public TypeTraversal(int maxDepth = 32, int maxStates = 1024)
        : this(maxDepth, maxStates, [], [], _ => false, []) { }

    private TypeTraversal(
        int maxDepth,
        int maxStates,
        Func<ITypeSymbol, IEnumerable<TypeEdge>>[] edges,
        Func<ITypeSymbol, bool>[] hidden,
        Func<ITypeSymbol, bool> stop,
        (CodeType Wrapper, int[] Arguments)[] opaque
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStates, 1);
        MaxDepth = maxDepth;
        MaxStates = maxStates;
        _edges = edges;
        _hidden = hidden;
        _stop = stop;
        _opaque = opaque;
    }

    /// <summary>The maximum shortest-path depth, including the seed at zero.</summary>
    public int MaxDepth { get; }

    /// <summary>The maximum visited constructed types per seed/context.</summary>
    public int MaxStates { get; }

    /// <summary>Adds finite consumer-selected edges. Null/error destination types preserve incompleteness.</summary>
    public TypeTraversal Follow(Func<ITypeSymbol, IEnumerable<TypeEdge>> edges) =>
        new(MaxDepth, MaxStates, [.. _edges, edges], _hidden, _stop, _opaque);

    /// <summary>Prunes outgoing edges at explicitly selected boundaries; excluded nodes may still be emitted.</summary>
    public TypeTraversal StopAt(Func<ITypeSymbol, bool> stop) =>
        new(MaxDepth, MaxStates, _edges, _hidden, type => _stop(type) || stop(type), _opaque);

    /// <summary>Traverses array elements; arrays are omitted from emitted models unless requested.</summary>
    public TypeTraversal UnwrapArrays(bool emitWrapper = false) =>
        Unwrap(
            type => type is IArrayTypeSymbol,
            type => [new(((IArrayTypeSymbol)type).ElementType)],
            emitWrapper
        );

    /// <summary>Traverses selected arguments of a configured generic definition. Unknown generic types are not transparent.</summary>
    public TypeTraversal Unwrap(
        CodeType wrapper,
        IReadOnlyList<int> arguments,
        bool emitWrapper = false
    )
    {
        var indices = arguments.ToArray();
        if (indices.Length == 0 || indices.Any(index => index < 0))
            throw new ArgumentException(
                "At least one non-negative generic argument index is required.",
                nameof(arguments)
            );
        return Unwrap(
            type => type is INamedTypeSymbol named && wrapper.Matches(named),
            type =>
            {
                var named = (INamedTypeSymbol)type;
                return indices.Select(index => new TypeEdge(
                    index < named.TypeArguments.Length ? named.TypeArguments[index] : null,
                    wrapper
                ));
            },
            emitWrapper
        );
    }

    /// <summary>Follows selected readable properties with actual generic substitutions. Defaults are public getters, inherited members, instance non-indexers; this is not a serializer contract.</summary>
    public TypeTraversal ThroughProperties(
        Func<IPropertySymbol, bool>? where = null,
        bool inherited = true,
        bool includeStatic = false,
        bool includeIndexers = false
    ) =>
        Follow(type =>
            Properties(type, inherited)
                .Where(property =>
                    property.GetMethod?.DeclaredAccessibility == Accessibility.Public
                    && property.DeclaredAccessibility == Accessibility.Public
                    && (includeStatic || !property.IsStatic)
                    && (includeIndexers || !property.IsIndexer)
                    && (where?.Invoke(property) ?? true)
                )
                .Select(property => new TypeEdge(property.Type, property))
        );

    /// <summary>Overrides finite traversal bounds without changing edges or wrapper policies.</summary>
    public TypeTraversal WithinLimits(int maxDepth, int maxTypes) =>
        new(maxDepth, maxTypes, _edges, _hidden, _stop, _opaque);

    /// <summary>Follows all generic type arguments, retaining the generic definition as edge evidence.</summary>
    public TypeTraversal ThroughGenericArguments() =>
        Follow(type =>
            type is INamedTypeSymbol named
                ? named.TypeArguments.Select(argument => new TypeEdge(
                    argument,
                    named.OriginalDefinition
                ))
                : []
        );

    /// <summary>Omits an opaque wrapper and follows only selected generic arguments at that node. Property and custom edges are suppressed regardless of call order; selected item types resume the ordinary policy. StopAt still prunes all edges.</summary>
    public TypeTraversal SkippingTypes(CodeType wrapper, params int[] arguments)
    {
        var selected = arguments.Length == 0 ? new[] { 0 } : arguments.ToArray();
        if (selected.Any(index => index < 0))
            throw new ArgumentOutOfRangeException(nameof(arguments));
        return new(MaxDepth, MaxStates, _edges, _hidden, _stop, [.. _opaque, (wrapper, selected)]);
    }

    internal bool Emit(ITypeSymbol type) =>
        !_hidden.Any(hidden => hidden(type))
        && !_opaque.Any(policy => policy.Wrapper.Matches(type));

    internal IEnumerable<TypeEdge> Edges(ITypeSymbol type)
    {
        if (_stop(type))
            return [];
        var policies = _opaque.Where(policy => policy.Wrapper.Matches(type)).ToArray();
        return policies.Length > 0 && type is INamedTypeSymbol named
            ? policies.SelectMany(policy =>
                policy.Arguments.Select(index => new TypeEdge(
                    index < named.TypeArguments.Length ? named.TypeArguments[index] : null,
                    policy.Wrapper
                ))
            )
            : _edges.SelectMany(edges => edges(type));
    }

    private TypeTraversal Unwrap(
        Func<ITypeSymbol, bool> matches,
        Func<ITypeSymbol, IEnumerable<TypeEdge>> edges,
        bool emitWrapper
    ) =>
        new(
            MaxDepth,
            MaxStates,
            [.. _edges, type => matches(type) ? edges(type) : []],
            emitWrapper ? _hidden : [.. _hidden, matches],
            _stop,
            _opaque
        );

    private static IEnumerable<IPropertySymbol> Properties(ITypeSymbol type, bool inherited)
    {
        if (type is not INamedTypeSymbol named)
            yield break;
        for (
            var current = named;
            current is not null;
            current = inherited ? current.BaseType : null
        )
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
                yield return property;
        if (inherited && named.TypeKind == TypeKind.Interface)
            foreach (var contract in named.AllInterfaces)
            foreach (var property in contract.GetMembers().OfType<IPropertySymbol>())
                yield return property;
    }
}
