using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

// A place in a function's body that runs another member, found once per analysis.
internal sealed class CallSite(
    AnalysisSource source,
    TextSpan span,
    IMethodSymbol target,
    bool deferred,
    bool dispatchable
)
{
    private static readonly object _unresolved = new();
    private object? _callee = _unresolved;

    public AnalysisSource Source { get; } = source;

    public TextSpan Span { get; } = span;

    // The bound target, constructed as at the site.
    public IMethodSymbol Target { get; } = target;

    // In a lambda or anonymous method, or a method group converted to a delegate.
    public bool Deferred { get; } = deferred;

    // A virtual, abstract or interface target not called through base.
    public bool Dispatchable { get; } = dispatchable;

    // Resolution is deterministic and the graph caches function instances, so a racing duplicate is harmless.
    public CodeFunction? Callee(CallGraph graph)
    {
        var callee = Volatile.Read(ref _callee);
        if (ReferenceEquals(callee, _unresolved))
        {
            callee = graph.Resolve(Target);
            Volatile.Write(ref _callee, callee);
        }

        return (CodeFunction?)callee;
    }
}

// One step a traversal may take from a site.
internal readonly record struct CallEdge(
    CallSite Site,
    IMethodSymbol Target,
    CodeFunction? Callee,
    CallEdgeKind Kind
);
