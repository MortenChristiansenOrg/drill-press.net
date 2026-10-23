using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Candidate overloads that add a typed parameter. Selection is evidence, never proof that changing an overload preserves behavior.</summary>
public static class MethodOverloads
{
    /// <summary>Enumerates same-name, same-static-shape overloads adding exactly one parameter of the requested type and retaining the other parameter types in order. Types compare by symbol identity, ignoring nullable annotations, and generic overloads compare with the method's own type parameters in place of theirs. By default, existing parameter names, ref kinds and generic arity must also match.</summary>
    /// <param name="method">The original bound method (extension methods are normalized).</param>
    /// <param name="parameterType">The single new parameter's type.</param>
    /// <param name="matchNames">Whether retained names, ref kinds and arity must match, suitable for named-argument transitions.</param>
    public static IEnumerable<IMethodSymbol> AddingParameter(
        this IMethodSymbol method,
        CodeType parameterType,
        bool matchNames = true
    )
    {
        var target = method.ReducedFrom ?? method;
        if (target.Parameters.Any(parameter => parameterType.Matches(parameter.Type)))
            return [];
        return target
            .ContainingType.GetMembers(target.Name)
            .OfType<IMethodSymbol>()
            .Where(candidate =>
                candidate.IsStatic == target.IsStatic
                && candidate.Parameters.Length == target.Parameters.Length + 1
                && candidate.Parameters.Count(parameter => parameterType.Matches(parameter.Type))
                    == 1
                && (
                    matchNames
                        ? candidate.Arity == target.Arity
                            && target.Parameters.All(parameter =>
                                candidate.Parameters.Any(other =>
                                    other.Name == parameter.Name
                                    && other.RefKind == parameter.RefKind
                                    && SymbolEqualityComparer.Default.Equals(
                                        other.Type,
                                        parameter.Type
                                    )
                                )
                            )
                        : RetainedTypes(candidate, target.OriginalDefinition)
                            .Where(type => !parameterType.Matches(type))
                            .SequenceEqual(
                                target.OriginalDefinition.Parameters.Select(parameter =>
                                    parameter.Type
                                ),
                                SymbolEqualityComparer.Default
                            )
                )
            );
    }

    // The candidate's parameter types, with its own type parameters replaced by the original method's, so generic
    // overloads compare by identity.
    private static IEnumerable<ITypeSymbol> RetainedTypes(
        IMethodSymbol candidate,
        IMethodSymbol original
    ) =>
        (
            candidate.Arity > 0 && candidate.Arity == original.Arity
                ? candidate.Construct([.. original.TypeParameters])
                : candidate
        ).Parameters.Select(parameter => parameter.Type);
}
