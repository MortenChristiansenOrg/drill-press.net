using Microsoft.CodeAnalysis;

namespace DrillPress.Relationships;

/// <summary>A source definition implementing an interface in one evaluated project context.</summary>
/// <param name="Project">The compilation owning the implementation.</param>
/// <param name="Symbol">The declared type, retaining abstractness and type kind for consumer filtering.</param>
public sealed record InterfaceImplementation(AnalysisProject Project, INamedTypeSymbol Symbol)
{
    /// <summary>The actual constructed contracts matched in this context; multiple constructions still count as one implementation definition.</summary>
    public IReadOnlyList<ImplementedContract> Contracts { get; init; } = [];
}

/// <summary>A matched constructed interface and whether it is explicitly declared in the implementing type's base list.</summary>
/// <param name="Symbol">The constructed contract in the implementation's compilation.</param>
/// <param name="IsDirect">False for contracts inherited only through a base class or another interface.</param>
public sealed record ImplementedContract(INamedTypeSymbol Symbol, bool IsDirect);

/// <summary>Implementation evidence from one compatible combination of evaluated projects.</summary>
/// <param name="Projects">The projects that can coexist in this view.</param>
/// <param name="Implementations">Distinct source definitions, including generated types and test projects.</param>
public sealed record InterfaceImplementationView(
    IReadOnlyList<AnalysisProject> Projects,
    IReadOnlyList<InterfaceImplementation> Implementations
);

/// <summary>A queryable implementation view retaining an ordinary source owner for zero-count diagnostics.</summary>
/// <param name="Owner">The interface whose compatible views were selected.</param>
/// <param name="Projects">The complete compatible graph, unaffected by implementation filtering.</param>
/// <param name="Implementations">The selected definition-based entries, possibly empty.</param>
public sealed record ImplementationView(
    CodeDeclaration Owner,
    IReadOnlyList<AnalysisProject> Projects,
    IReadOnlyList<InterfaceImplementation> Implementations
);

/// <summary>Evidence for an actual override edge to a configured ancestor.</summary>
/// <param name="Method">The ordinary source overriding method.</param>
/// <param name="Ancestor">The matched bound ancestor, retaining generic substitution.</param>
/// <param name="Distance">One for the directly overridden member.</param>
public sealed record MethodOverride(CodeMethod Method, IMethodSymbol Ancestor, int Distance);

/// <summary>Controls which compiler override edges participate in a query.</summary>
public enum OverrideSearch
{
    /// <summary>Match only the directly overridden method.</summary>
    Immediate,

    /// <summary>Follow the complete override chain.</summary>
    AnyAncestor,
}

/// <summary>Source body shape, independent of claims about runtime effects.</summary>
public enum MethodBodyShape
{
    /// <summary>An abstract, extern or otherwise bodyless method.</summary>
    Missing,

    /// <summary>A block with no statement nodes; comments do not count as statements.</summary>
    EmptyBlock,

    /// <summary>A block with at least one statement, including empty or local-function statements.</summary>
    NonEmptyBlock,

    /// <summary>An arrow expression body.</summary>
    Expression,
}
