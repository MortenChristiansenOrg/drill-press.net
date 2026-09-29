using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>An exact contextual overload pair and explicit retained parameter map. A method-family predicate is not an exact pair.</summary>
public sealed class MethodTransition
{
    private readonly Func<AnalysisProject, MethodPair?> _resolve;

    /// <summary>Supplies fully constructed compiler symbols in each original compilation and a before-name to after-name map, including extension receivers.</summary>
    public MethodTransition(
        Func<AnalysisProject, MethodPair?> resolve,
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
        _resolve = resolve;
        Parameters = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(parameters)
        );
    }

    /// <summary>The retained parameter correspondence, independent from source argument order.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    internal MethodPair? Resolve(AnalysisProject project) => _resolve(project);
}
