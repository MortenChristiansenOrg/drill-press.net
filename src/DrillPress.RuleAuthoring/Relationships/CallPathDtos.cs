using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>One followed edge of a call path: a site in the caller and the member it runs.</summary>
/// <param name="Caller">The function containing the site.</param>
/// <param name="Target">The member the step runs: the bound target, or the override, implementation or configured member it reaches.</param>
/// <param name="Callee">The source function for <paramref name="Target"/>; null for members declared in metadata.</param>
/// <param name="Kind">Why the step was followed.</param>
/// <param name="ViaDelegate">Whether the site is in a lambda or anonymous method, or converts a method group to a delegate, so it runs only if the delegate runs.</param>
/// <param name="Source">The document containing the site, which differs from the caller's for a field initializer in another partial part.</param>
/// <param name="Location">The call, creation, accessor use, operator or delegate conversion.</param>
public sealed record CallStep(
    CodeFunction Caller,
    IMethodSymbol Target,
    CodeFunction? Callee,
    CallEdgeKind Kind,
    bool ViaDelegate,
    AnalysisSource Source,
    SourceLocation Location
) : ICodeElement;

/// <summary>The steps from a query's starting function to the first site that runs its target, shortest first.</summary>
/// <param name="Steps">The followed steps; the last one runs the target.</param>
public sealed record CallPath(IReadOnlyList<CallStep> Steps) : ICodeElement
{
    /// <summary>Whether every step is a statically bound call outside delegates, so the path runs whenever its sites run.</summary>
    public bool IsBound => Steps.All(step => step.Kind == CallEdgeKind.Bound && !step.ViaDelegate);

    /// <summary>The first step's document.</summary>
    public AnalysisSource Source => Steps[0].Source;

    /// <summary>The first step's site in the starting function.</summary>
    public SourceLocation Location => Steps[0].Location;
}

/// <summary>Why a call-path step was followed.</summary>
public enum CallEdgeKind
{
    /// <summary>The compiler bound the site to the target.</summary>
    Bound,

    /// <summary>A source override or implementation of a virtual, abstract or interface target, followed by <see cref="CallPaths.ThroughDispatch"/>.</summary>
    Dispatch,

    /// <summary>A consumer edge from <see cref="CallPaths.WithEdge"/>.</summary>
    Configured,
}

/// <summary>The kind of source declaration a function's body belongs to.</summary>
public enum FunctionKind
{
    /// <summary>An ordinary method, including explicit interface implementations and extension methods.</summary>
    Method,

    /// <summary>A local function.</summary>
    LocalFunction,

    /// <summary>An instance constructor: declared, primary, or the implicit parameterless constructor of a source type.</summary>
    Constructor,

    /// <summary>A property, indexer or event accessor, including an expression-bodied property's getter.</summary>
    Accessor,

    /// <summary>A user-defined operator or conversion.</summary>
    Operator,
}

internal sealed record ConfiguredCall(CodeMember Call, CodeMember Target);
