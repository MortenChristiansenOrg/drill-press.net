using Microsoft.CodeAnalysis;

namespace DrillPress.Relationships;

/// <summary>A source definition implementing an interface in one evaluated project context.</summary>
/// <param name="Project">The compilation owning the implementation.</param>
/// <param name="Symbol">The declared type, retaining abstractness and type kind for consumer filtering.</param>
public sealed record InterfaceImplementation(AnalysisProject Project, INamedTypeSymbol Symbol);

/// <summary>Implementation evidence from one compatible combination of evaluated projects.</summary>
/// <param name="Projects">The projects that can coexist in this view.</param>
/// <param name="Implementations">Distinct source definitions, including generated types and test projects.</param>
public sealed record InterfaceImplementationView(
    IReadOnlyList<AnalysisProject> Projects,
    IReadOnlyList<InterfaceImplementation> Implementations
);
