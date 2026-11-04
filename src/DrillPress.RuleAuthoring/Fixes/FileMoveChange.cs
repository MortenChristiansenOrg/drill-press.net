using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>A file rename or move in one affected compilation, ready for a consumer's proof.</summary>
public sealed class FileMoveChange
{
    internal FileMoveChange(
        AnalysisSource source,
        string from,
        string to,
        IReadOnlyList<IMethodSymbol> callerFilePathMethods,
        RewriteContext rewrite
    )
    {
        Source = source;
        From = from;
        To = to;
        CallerFilePathMethods = callerFilePathMethods;
        Rewrite = rewrite;
    }

    /// <summary>The moved file's membership in the compilation being validated.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The captured path before the move.</summary>
    public string From { get; }

    /// <summary>The path after the move, in the same form as <see cref="From"/>.</summary>
    public string To { get; }

    /// <summary>Methods, constructors and indexer accessors called in the file whose omitted <c>[CallerFilePath]</c> argument the move changes, as bound after all edits in the batch.</summary>
    public IReadOnlyList<IMethodSymbol> CallerFilePathMethods { get; }

    /// <summary>The compilation after every edit validated together with the move; the move itself does not change its text.</summary>
    public RewriteContext Rewrite { get; }
}
