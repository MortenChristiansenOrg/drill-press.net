using DrillPress.Operations;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Fixes;

/// <summary>Context-local evidence for one argument deletion and verified overload transition.</summary>
public sealed class ArgumentRemovalEvidence
{
    internal ArgumentRemovalEvidence(
        RewriteEvidence rewrite,
        IInvocationOperation before,
        IInvocationOperation after,
        CodeArgument removed,
        MethodPair expected
    )
    {
        Rewrite = rewrite;
        Before = before;
        After = after;
        Removed = removed;
        Expected = expected;
    }

    /// <summary>The complete batch mapping and semantic models.</summary>
    public RewriteEvidence Rewrite { get; }

    /// <summary>The actual original call, including compiler-supplied arguments.</summary>
    public IInvocationOperation Before { get; }

    /// <summary>The rebound call, including all new/defaulted arguments and conversions.</summary>
    public IInvocationOperation After { get; }

    /// <summary>The removed bound value, with its property/getter, type, conversion and source evidence available through Operation.</summary>
    public CodeArgument Removed { get; }

    /// <summary>The configured exact pair as resolved in this original context.</summary>
    public MethodPair Expected { get; }
}
