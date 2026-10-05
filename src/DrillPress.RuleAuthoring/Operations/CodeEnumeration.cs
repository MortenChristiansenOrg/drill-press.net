using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>A written foreach or await foreach occurrence, separating collection evaluation from advancement and body execution.</summary>
public sealed class CodeEnumeration(AnalysisSource source, CommonForEachStatementSyntax syntax)
    : ICodeElement
{
    private ForEachStatementInfo? _information;

    /// <summary>The original document and independently evaluated project context.</summary>
    public AnalysisSource Source { get; } = source;

    /// <summary>The complete written loop, including ordinary and deconstruction forms.</summary>
    public CommonForEachStatementSyntax Syntax { get; } = syntax;

    /// <summary>The written collection value before contextual conversion; evaluating it does not prove advancement.</summary>
    public CodeExpression SourceExpression => new(Source, Syntax.Expression);

    /// <summary>The independently selectable loop body; an empty enumeration may never enter it.</summary>
    public CodeNode<StatementSyntax> Body => new(Source, Syntax.Statement);

    /// <summary>Whether this is await foreach rather than synchronous foreach.</summary>
    public bool IsAsync => !Syntax.AwaitKeyword.IsKind(SyntaxKind.None);

    /// <summary>Whether the collection and loop bind successfully; this does not imply coverage support.</summary>
    public bool IsResolved =>
        SourceExpression.IsResolved
        && Source.Model.GetOperation(Syntax, Source.Project.CancellationToken)
            is IForEachLoopOperation;

    /// <summary>The compiler-selected enumerator acquisition method; absent for indexed array/string lowering or failed binding.</summary>
    public IMethodSymbol? GetEnumeratorMethod =>
        IsResolved && !UsesIndexedLowering ? Information.GetEnumeratorMethod : null;

    /// <summary>The compiler-selected MoveNext or MoveNextAsync method; absent for indexed lowering or failed binding.</summary>
    public IMethodSymbol? MoveNextMethod =>
        IsResolved && !UsesIndexedLowering ? Information.MoveNextMethod : null;

    /// <summary>The compiler-selected element type, including deconstruction input types.</summary>
    public ITypeSymbol? ElementType => IsResolved ? Information.ElementType : null;

    /// <summary>Diagnostics identify the collection expression while advancement evidence retains its separate condition point.</summary>
    public SourceLocation Location => SourceExpression.Location;

    private bool UsesIndexedLowering =>
        SourceExpression.Type is IArrayTypeSymbol
        || SourceExpression.Type?.SpecialType == SpecialType.System_String;

    private ForEachStatementInfo Information =>
        _information ??= Source.Model.GetForEachStatementInfo(Syntax);
}
