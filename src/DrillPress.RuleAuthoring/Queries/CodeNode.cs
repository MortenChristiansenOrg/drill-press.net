using Microsoft.CodeAnalysis;

namespace DrillPress.Queries;

/// <summary>A syntax candidate with precise physical reporting and its original semantic context.</summary>
public sealed class CodeNode<TSyntax>(AnalysisSource source, TSyntax syntax) : ICodeElement
    where TSyntax : SyntaxNode
{
    /// <summary>The captured document membership, including its compilation.</summary>
    public AnalysisSource Source { get; } = source;

    /// <summary>The original syntax; unresolved code remains available to syntax-only rules.</summary>
    public TSyntax Syntax { get; } = syntax;

    /// <summary>The complete node span, excluding exterior trivia.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Span);

    /// <summary>The compiler operation, absent for syntax without an operation.</summary>
    public IOperation? Operation =>
        Source.Model.GetOperation(Syntax, Source.Project.CancellationToken);

    /// <summary>Compiler type and nullable flow information at this syntax position.</summary>
    public TypeInfo TypeInfo => Source.Model.GetTypeInfo(Syntax, Source.Project.CancellationToken);

    /// <summary>Returns a compile-time constant, including a constant null, without guessing from source spelling.</summary>
    public Optional<object?> Constant =>
        Source.Model.GetConstantValue(Syntax, Source.Project.CancellationToken);
}
