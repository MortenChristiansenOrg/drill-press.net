namespace DrillPress;

/// <summary>
/// Immutable consumer configuration for call-path queries: which edges <see cref="CodeFunction.Reaches"/>,
/// <see cref="CodeFunction.Callees"/> and <see cref="CodeFunction.PathTo"/> follow beyond statically bound calls.
/// </summary>
/// <remarks>
/// <see cref="CodeFunction.Reaches"/> caches its answers per analysis, target and configuration instance, so keep
/// configured instances in static fields. A negative answer never proves that runtime execution can't reach an API.
/// </remarks>
public sealed class CallPaths
{
    private CallPaths(
        bool delegates,
        bool dispatch,
        IReadOnlyList<ConfiguredCall> edges,
        Func<CodeFunction, bool>? boundary
    )
    {
        Delegates = delegates;
        Dispatch = dispatch;
        Edges = edges;
        Boundary = boundary;
    }

    /// <summary>
    /// Follows statically bound calls to every source function they resolve to: methods, local functions,
    /// constructors (with their initializers and the field and property initializers they run), property, indexer and
    /// event accessors, and user-defined operators and conversions.
    /// </summary>
    public static CallPaths Bound { get; } = new(false, false, [], null);

    internal bool Delegates { get; }

    internal bool Dispatch { get; }

    internal IReadOnlyList<ConfiguredCall> Edges { get; }

    internal Func<CodeFunction, bool>? Boundary { get; }

    /// <summary>
    /// Also follows calls in the lambdas and anonymous methods a function creates, and method groups it converts to
    /// delegates. The SDK can't tell whether the delegate runs, so these steps have <see cref="CallStep.ViaDelegate"/> set.
    /// </summary>
    public CallPaths ThroughDelegates() => new(true, Dispatch, Edges, Boundary);

    /// <summary>
    /// Also follows virtual, abstract and interface calls to the source overrides and implementations that can run in
    /// the caller's process: those in a compatible project view that contains the caller, keeping separate evaluation
    /// contexts and target frameworks apart. Calls to members declared in metadata, such as <c>IDisposable.Dispose</c>,
    /// and calls through <c>base</c> are not dispatched.
    /// </summary>
    /// <remarks>A call through a closed generic contract, such as <c>IHandler&lt;Order&gt;</c>, reaches only implementations of that construction.</remarks>
    public CallPaths ThroughDispatch() => new(Delegates, true, Edges, Boundary);

    /// <summary>
    /// Treats every call to <paramref name="call"/> as also running <paramref name="target"/>, for edges the compiler
    /// can't bind, such as dependency injection, reflection, messaging or framework callbacks. The target resolves to
    /// its source declarations, overrides and implementations in projects that share a compatible view with the caller.
    /// </summary>
    /// <param name="call">The configured API whose calls, accessor uses or constructions start the edge.</param>
    /// <param name="target">The member the edge runs, such as a handler interface method or a constructor.</param>
    public CallPaths WithEdge(CodeMember call, CodeMember target) =>
        new(Delegates, Dispatch, [.. Edges, new(call, target)], Boundary);

    /// <summary>
    /// Doesn't follow calls into the functions <paramref name="boundary"/> selects, such as approved helpers. The call
    /// into a boundary still matches a target and <see cref="CodeFunction.Callees"/> lists the boundary, but its own
    /// calls are searched only when the query starts there.
    /// </summary>
    public CallPaths StopAt(Func<CodeFunction, bool> boundary) =>
        new(
            Delegates,
            Dispatch,
            Edges,
            Boundary is { } existing
                ? function => existing(function) || boundary(function)
                : boundary
        );
}
