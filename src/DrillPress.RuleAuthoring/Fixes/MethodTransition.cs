using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>An exact contextual overload pair with retained parameter mapping, resolved explicitly or from descriptors at the actual call.</summary>
public sealed class MethodTransition
{
    private readonly Func<AnalysisProject, MethodPair?>? _resolve;
    private readonly CodeMember? _before;
    private readonly CodeMember? _after;
    private readonly bool _infer;

    /// <summary>Supplies fully constructed compiler symbols and a before-name to after-name map, including extension receivers.</summary>
    public MethodTransition(
        Func<AnalysisProject, MethodPair?> resolve,
        IReadOnlyDictionary<string, string> parameters
    )
    {
        _resolve = resolve;
        Parameters = Copy(parameters);
    }

    /// <summary>Resolves an unambiguous target overload using the original call's constructed generic arguments. Omitted mappings are inferred only for retained parameters with identical names and types.</summary>
    public MethodTransition(
        CodeMember from,
        CodeMember to,
        IReadOnlyDictionary<string, string>? parameters = null
    )
    {
        _before = from;
        _after = to;
        _infer = parameters is null;
        Parameters = Copy(parameters ?? new Dictionary<string, string>());
    }

    /// <summary>The explicitly configured correspondence; empty when contextual identity mapping is requested.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    internal MethodPair? Resolve(AnalysisProject project, IMethodSymbol actual)
    {
        if (_resolve is not null)
            return _resolve(project);
        if (_before?.Matches(actual) != true || _after is null)
            return null;
        var type = _after.DeclaringType.Matches(actual.ContainingType)
            ? actual.ContainingType
            : _after.DeclaringType.Resolve(project.Compilation) as INamedTypeSymbol;
        if (type is null)
            return null;
        var targets = type.GetMembers(_after.Name)
            .OfType<IMethodSymbol>()
            .Where(method => method.Arity == actual.Arity)
            .Select(method =>
                method.Arity == 0 ? method : method.Construct(actual.TypeArguments.ToArray())
            )
            .Where(_after.Matches)
            .ToArray();
        return targets is [var target] ? new(actual, target) : null;
    }

    internal IReadOnlyDictionary<string, string>? ParametersFor(MethodPair pair)
    {
        if (!_infer)
            return Parameters;
        var result = new Dictionary<string, string>();
        foreach (var target in pair.After.Parameters)
        {
            var source = pair
                .Before.Parameters.Where(parameter => parameter.Name == target.Name)
                .ToArray();
            if (
                source is not [var original]
                || original.RefKind != target.RefKind
                || !SymbolEqualityComparer.Default.Equals(original.Type, target.Type)
            )
                return null;
            result.Add(original.Name, target.Name);
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> Copy(
        IReadOnlyDictionary<string, string> parameters
    )
    {
        if (
            parameters.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)
            )
            || parameters.Values.Distinct().Count() != parameters.Count
        )
            throw new ArgumentException(
                "Parameter mappings require distinct non-empty names.",
                nameof(parameters)
            );
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(parameters)
        );
    }
}
