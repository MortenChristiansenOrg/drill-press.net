using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Candidate overloads that add a typed parameter. Selection is evidence, never proof that changing an overload preserves behavior.</summary>
public static class MethodOverloads
{
    /// <summary>Enumerates same-name, same-static-shape overloads adding exactly one parameter of the requested type and retaining the other parameter types in order. Generic declarations compare original type spellings. By default, existing parameter names, ref kinds and generic arity must also match.</summary>
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
                        : candidate
                            .Parameters.Where(parameter => !parameterType.Matches(parameter.Type))
                            .Select(parameter => parameter.Type.ToDisplayString())
                            .SequenceEqual(
                                target.OriginalDefinition.Parameters.Select(parameter =>
                                    parameter.Type.ToDisplayString()
                                )
                            )
                )
            );
    }
}
