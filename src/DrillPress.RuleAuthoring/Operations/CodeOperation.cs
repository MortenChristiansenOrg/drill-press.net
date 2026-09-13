using Microsoft.CodeAnalysis;

namespace DrillPress.Operations;

/// <summary>A compiler operation with its original source membership and exact location.</summary>
public sealed class CodeOperation<TOperation>(AnalysisSource source, TOperation operation)
    : ICodeElement
    where TOperation : IOperation
{
    /// <summary>The document supplying the operation's semantic model.</summary>
    public AnalysisSource Source { get; } = source;

    /// <summary>The bound operation. Invalid operations remain visible; rules must withhold conclusions requiring successful binding.</summary>
    public TOperation Operation { get; } = operation;

    /// <summary>The operation syntax span; implicit operations can share a location with their parent.</summary>
    public SourceLocation Location => Source.Locate(Operation.Syntax.Span);

    /// <summary>The nearest containing declared symbol, where the compiler can resolve it.</summary>
    public ISymbol? ContainingSymbol =>
        Source.Model.GetEnclosingSymbol(
            Operation.Syntax.SpanStart,
            Source.Project.CancellationToken
        );
}
