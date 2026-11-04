using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

// Resolves dispatch and configured edges to source functions within the project views a caller can run in. Each
// answer is cached per target and calling context, and the type indexes are built only when an option asks for them.
internal sealed class CallTargets(AnalysisSolution solution, CallGraph graph)
{
    private static readonly SymbolDisplayFormat _identity =
        SymbolDisplayFormat.FullyQualifiedFormat;
    private readonly CompilationViews _views = new(solution);
    private readonly ConcurrentDictionary<string, Lazy<SourceTypes>> _types = new();
    private readonly ConcurrentDictionary<DispatchKey, Lazy<Target[]>> _dispatch = new();
    private readonly ConcurrentDictionary<(ConfiguredCall, string), Lazy<Target[]>> _configured =
        new();

    // Source overrides and implementations of a virtual, abstract or interface member declared in source.
    public IReadOnlyList<Target> Implementations(IMethodSymbol target, AnalysisProject caller)
    {
        var definition = CallGraph.Normalize(target);
        if (
            definition.DeclaringSyntaxReferences.FirstOrDefault() is not { } reference
            || graph.SourceOf(reference.SyntaxTree, definition)?.Project is not { } owner
        )
            return [];
        var contract = target.ContainingType;
        var closed =
            contract.IsGenericType && !ContainsTypeParameter(contract)
                ? contract.ToDisplayString(_identity)
                : null;
        return _dispatch
            .GetOrAdd(
                new(reference.SyntaxTree, reference.Span.Start, caller.Snapshot.ContextId, closed),
                _ => new(() => FindImplementations(definition, owner, caller, closed))
            )
            .Value;
    }

    private Target[] FindImplementations(
        IMethodSymbol definition,
        AnalysisProject owner,
        AnalysisProject caller,
        string? closed
    )
    {
        var declaringType = definition.ContainingType.OriginalDefinition;
        var metadataName = CodeType.MetadataNameOf(declaringType);
        var result = new List<Target>();
        var seen = new HashSet<CodeFunction>();
        foreach (var project in Coexisting(owner, caller))
        {
            var type = InterfaceImplementations
                .ResolveAssembly(project, owner)
                ?.GetTypeByMetadataName(metadataName);
            if (type is null || !Types(project).Derived.TryGetValue(type, out var derived))
                continue;
            foreach (var candidate in derived)
            {
                var implementations =
                    declaringType.TypeKind == TypeKind.Interface
                        ? ImplementationsIn(candidate, type, definition, closed)
                        : Override(candidate, type, definition, closed);
                foreach (var implementation in implementations)
                {
                    if (
                        !SameDeclaration(CallGraph.Normalize(implementation), definition)
                        && graph.Resolve(implementation) is { } function
                        && seen.Add(function)
                    )
                        result.Add(new(implementation, function));
                }
            }
        }

        return [.. result];
    }

    // The projects in any compatible view that contains both the declaring project and the caller.
    private IEnumerable<AnalysisProject> Coexisting(
        AnalysisProject owner,
        AnalysisProject caller
    ) => _views.For(owner).Where(view => view.Contains(caller)).SelectMany(view => view).Distinct();

    private static IEnumerable<IMethodSymbol> ImplementationsIn(
        INamedTypeSymbol type,
        INamedTypeSymbol contract,
        IMethodSymbol definition,
        string? closed
    )
    {
        foreach (var implemented in type.AllInterfaces)
        {
            if (
                !SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, contract)
                || closed is not null && implemented.ToDisplayString(_identity) != closed
            )
                continue;
            foreach (var member in implemented.GetMembers(definition.Name))
            {
                if (
                    member is IMethodSymbol method
                    && SameDeclaration(method.OriginalDefinition, definition)
                    && type.FindImplementationForInterfaceMember(method)
                        is IMethodSymbol implementation
                )
                    yield return implementation;
            }
        }
    }

    // The override an instance of the type runs, found from the most derived type upward.
    private static IEnumerable<IMethodSymbol> Override(
        INamedTypeSymbol type,
        INamedTypeSymbol declaringType,
        IMethodSymbol definition,
        string? closed
    )
    {
        if (
            closed is not null
            && Bases(type)
                .FirstOrDefault(baseType =>
                    SymbolEqualityComparer.Default.Equals(
                        baseType.OriginalDefinition,
                        declaringType
                    )
                )
                ?.ToDisplayString(_identity) != closed
        )
            yield break;
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(definition.Name).OfType<IMethodSymbol>())
            {
                for (var overridden = member.OverriddenMethod; overridden is not null; )
                {
                    if (SameDeclaration(CallGraph.Normalize(overridden), definition))
                    {
                        yield return member;
                        yield break;
                    }

                    overridden = overridden.OverriddenMethod;
                }
            }

            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, declaringType))
                yield break;
        }
    }

    private static IEnumerable<INamedTypeSymbol> Bases(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            yield return current;
    }

    // Source declarations of a configured target, with their overrides and implementations, in any project that
    // shares a compatible view with the caller.
    public IReadOnlyList<Target> Configured(ConfiguredCall edge, AnalysisProject caller) =>
        _configured
            .GetOrAdd(
                (edge, caller.Snapshot.ContextId),
                _ => new(() => FindConfigured(edge.Target, caller))
            )
            .Value;

    private Target[] FindConfigured(CodeMember target, AnalysisProject caller)
    {
        var result = new List<Target>();
        var seen = new HashSet<CodeFunction>();
        foreach (var project in _views.For(caller).SelectMany(view => view).Distinct())
        {
            foreach (var type in Types(project).Declared)
            {
                solution.CancellationToken.ThrowIfCancellationRequested();
                foreach (var member in Members(type, target.Name))
                {
                    if (
                        (CallGraph.Matches(target, member) || OverridesMatching(member, target))
                        && graph.Resolve(member) is { } function
                        && seen.Add(function)
                    )
                        result.Add(new(member, function));
                }

                foreach (var contract in type.AllInterfaces)
                {
                    foreach (var member in Members(contract, target.Name))
                    {
                        if (
                            CallGraph.Matches(target, member)
                            && type.FindImplementationForInterfaceMember(member)
                                is IMethodSymbol implementation
                            && graph.Resolve(implementation) is { } function
                            && seen.Add(function)
                        )
                            result.Add(new(implementation, function));
                    }
                }
            }
        }

        return [.. result];
    }

    private static bool OverridesMatching(IMethodSymbol method, CodeMember target)
    {
        for (var overridden = method.OverriddenMethod; overridden is not null; )
        {
            if (CallGraph.Matches(target, overridden))
                return true;
            overridden = overridden.OverriddenMethod;
        }

        return false;
    }

    // Methods, constructors and accessors with the member name.
    private static IEnumerable<IMethodSymbol> Members(INamedTypeSymbol type, string name)
    {
        foreach (var member in type.GetMembers(name))
        {
            switch (member)
            {
                case IMethodSymbol method:
                    yield return method;
                    break;
                case IPropertySymbol property:
                    if (property.GetMethod is { } getter)
                        yield return getter;
                    if (property.SetMethod is { } setter)
                        yield return setter;
                    break;
                case IEventSymbol @event:
                    if (@event.AddMethod is { } add)
                        yield return add;
                    if (@event.RemoveMethod is { } remove)
                        yield return remove;
                    break;
            }
        }
    }

    private SourceTypes Types(AnalysisProject project) =>
        _types
            .GetOrAdd(
                project.Snapshot.ContextId,
                _ => new(() => solution.Shared("call-paths", () => IndexTypes(project)))
            )
            .Value;

    // Types declared in the project's own sources, including generated ones, by each base type and interface.
    private SourceTypes IndexTypes(AnalysisProject project)
    {
        var declared = new List<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var derived = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(
            SymbolEqualityComparer.Default
        );
        foreach (var source in project.Sources)
        {
            foreach (var syntax in source.Nodes<TypeDeclarationSyntax>())
            {
                solution.CancellationToken.ThrowIfCancellationRequested();
                if (
                    source.Model.GetDeclaredSymbol(syntax, solution.CancellationToken)
                        is not INamedTypeSymbol type
                    || !seen.Add(type)
                )
                    continue;
                declared.Add(type);
                foreach (var ancestor in Bases(type).Concat(type.AllInterfaces))
                {
                    if (!derived.TryGetValue(ancestor.OriginalDefinition, out var types))
                    {
                        types = [];
                        derived.Add(ancestor.OriginalDefinition, types);
                    }

                    if (!types.Contains(type, SymbolEqualityComparer.Default))
                        types.Add(type);
                }
            }
        }

        return new(declared, derived);
    }

    private static bool SameDeclaration(IMethodSymbol left, IMethodSymbol right) =>
        left.DeclaringSyntaxReferences.FirstOrDefault() is { } first
        && right.DeclaringSyntaxReferences.FirstOrDefault() is { } second
        && first.SyntaxTree == second.SyntaxTree
        && first.Span == second.Span;

    private static bool ContainsTypeParameter(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
            IPointerTypeSymbol pointer => ContainsTypeParameter(pointer.PointedAtType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter)
                || named.ContainingType is { } outer && ContainsTypeParameter(outer),
            _ => false,
        };

    internal readonly record struct Target(IMethodSymbol Symbol, CodeFunction Function);

    private sealed record SourceTypes(
        IReadOnlyList<INamedTypeSymbol> Declared,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> Derived
    );

    private readonly record struct DispatchKey(
        SyntaxTree Tree,
        int Start,
        string Context,
        string? Closed
    );
}
