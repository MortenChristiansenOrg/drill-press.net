using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>Statically visible minimal API mapping, route path, configuration chains and handler. Unknown values remain null.</summary>
/// <param name="Mapping">The route or group mapping call.</param>
/// <param name="Path">Concatenated constant group prefixes and route pattern; null when unknown.</param>
/// <param name="Chain">Calls configuring the mapping result.</param>
/// <param name="GroupChain">Calls configuring its visible parent groups.</param>
/// <param name="HandlerArgument">Unwrapped method reference or anonymous handler operation.</param>
/// <param name="Handler">Bound handler method, including an anonymous function's symbol.</param>
public sealed record CodeEndpointRoute(
    CodeInvocation Mapping,
    string? Path,
    IReadOnlyList<IInvocationOperation> Chain,
    IReadOnlyList<IInvocationOperation> GroupChain,
    IOperation? HandlerArgument,
    IMethodSymbol? Handler
) : ICodeElement
{
    /// <summary>The mapping's original compilation membership.</summary>
    public AnalysisSource Source => Mapping.Source;

    /// <summary>The mapping call's method-name location.</summary>
    public SourceLocation Location => Mapping.Location;
}
