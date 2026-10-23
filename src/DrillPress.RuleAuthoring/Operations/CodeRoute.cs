using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>A minimal API route or route group: its mapping call, path, handler and the calls that configure it. What can't be read statically remains null.</summary>
public sealed class CodeRoute : ICodeElement
{
    internal CodeRoute(
        CodeInvocation mapping,
        string? path,
        IReadOnlyList<CodeInvocation> configuration,
        IReadOnlyList<CodeInvocation> groupConfiguration,
        CodeExpression? handlerExpression,
        IMethodSymbol? handler
    )
    {
        Mapping = mapping;
        Path = path;
        Configuration = configuration;
        GroupConfiguration = groupConfiguration;
        HandlerExpression = handlerExpression;
        Handler = handler;
    }

    /// <summary>The <c>MapGet</c>, <c>MapPost</c>, <c>MapGroup</c> or other mapping call.</summary>
    public CodeInvocation Mapping { get; }

    /// <summary>The route pattern after the constant prefixes of its groups, such as <c>/api/orders/{id}</c>; null when a prefix or the pattern isn't a constant.</summary>
    public string? Path { get; }

    /// <summary>The calls chained onto the mapping, such as <c>.WithName(...)</c> and <c>.RequireAuthorization()</c>.</summary>
    public IReadOnlyList<CodeInvocation> Configuration { get; }

    /// <summary>The calls chained onto the groups the route is mapped on, as far as their builders can be followed through locals.</summary>
    public IReadOnlyList<CodeInvocation> GroupConfiguration { get; }

    /// <summary>The written handler, a lambda or method group; null for a route group.</summary>
    public CodeExpression? HandlerExpression { get; }

    /// <summary>The handler method, including a lambda's own symbol; null for a route group or an unresolved handler.</summary>
    public IMethodSymbol? Handler { get; }

    /// <summary>The mapping's original compilation membership.</summary>
    public AnalysisSource Source => Mapping.Source;

    /// <summary>The mapping call's method name.</summary>
    public SourceLocation Location => Mapping.Location;

    /// <summary>Whether a call chained onto the mapping binds to the member, such as <c>RequireAuthorization</c>; with <paramref name="includeGroups"/>, also one chained onto its groups.</summary>
    /// <remarks>Reads the configuration calls that are written, not the endpoint metadata they produce.</remarks>
    public bool IsConfiguredWith(CodeMember member, bool includeGroups = false) =>
        Configuration.Any(call => call.Calls(member))
        || includeGroups && GroupConfiguration.Any(call => call.Calls(member));
}
