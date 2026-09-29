using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>A complete if/conditional-expression test and its syntactic branches; presence is not guaranteed execution.</summary>
public sealed class CodeCondition : ICodeElement
{
    private readonly SyntaxNode _whenTrue;
    private readonly SyntaxNode? _whenFalse;

    internal CodeCondition(CodeExpression expression, SyntaxNode whenTrue, SyntaxNode? whenFalse)
    {
        Expression = expression;
        _whenTrue = whenTrue;
        _whenFalse = whenFalse;
    }

    /// <summary>The complete condition, never an isolated leaf from a compound Boolean expression.</summary>
    public CodeExpression Expression { get; }

    /// <summary>The condition's compilation membership.</summary>
    public AnalysisSource Source => Expression.Source;

    /// <summary>The complete test span for reporting.</summary>
    public SourceLocation Location => Expression.Location;

    /// <summary>Gets an existing syntactic branch, excluding nested function bodies by default. An absent else returns null.</summary>
    public CodeBody? Branch(bool outcome, NestedFunctions nested = NestedFunctions.Exclude) =>
        (outcome ? _whenTrue : _whenFalse) is { } branch ? new(Source, branch, nested) : null;
}
