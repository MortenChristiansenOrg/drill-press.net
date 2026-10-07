using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>A written catch clause with its caught type, optional filter and body.</summary>
public sealed class CodeCatch : ICodeElement
{
    internal CodeCatch(AnalysisSource source, CatchClauseSyntax syntax)
    {
        Source = source;
        Syntax = syntax;
    }

    /// <summary>The original document and compilation membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The complete written catch clause.</summary>
    public CatchClauseSyntax Syntax { get; }

    /// <summary>The declared exception type; null for <c>catch { }</c>, which catches every exception.</summary>
    public ITypeSymbol? ExceptionType =>
        Syntax.Declaration is { } declaration
            ? Source.Model.GetTypeInfo(declaration.Type, Source.Project.CancellationToken).Type
            : null;

    /// <summary>The declared exception variable name, if any.</summary>
    public string? VariableName =>
        Syntax.Declaration?.Identifier is { RawKind: not 0 } identifier
            ? identifier.ValueText
            : null;

    /// <summary>Matches the declared exception type exactly; derived exception types are not the same catch.</summary>
    public bool Catches(CodeType type) => ExceptionType is { } actual && type.Matches(actual);

    /// <summary>Matches the declared exception type exactly.</summary>
    public bool Catches<T>()
        where T : Exception => Catches(CodeType.Of<T>());

    /// <summary>Whether the clause catches every exception: no declaration, <c>System.Exception</c> or <c>System.Object</c>. A filter can still decline exceptions.</summary>
    public bool CatchesAnyException =>
        Syntax.Declaration is null
        || ExceptionType is { SpecialType: SpecialType.System_Object }
        || Catches(CodeType.Of<Exception>());

    /// <summary>Whether a <c>when</c> filter is written.</summary>
    public bool HasFilter => Syntax.Filter is not null;

    /// <summary>The written filter condition, if any.</summary>
    public CodeExpression? Filter =>
        Syntax.Filter is { } filter ? new(Source, filter.FilterExpression) : null;

    /// <summary>The handler body, excluding nested functions.</summary>
    public CodeBody Body => new(Source, Syntax.Block, NestedFunctions.Exclude);

    /// <summary>Whether the handler contains no statements; comments do not count.</summary>
    public bool IsEmpty => Syntax.Block.Statements.Count == 0;

    /// <summary>Whether the handler rethrows the caught exception with <c>throw;</c>, outside nested handlers and functions.</summary>
    public bool Rethrows =>
        Body.Nodes<ThrowStatementSyntax>()
            .Any(node =>
                node.Syntax.Expression is null
                && node.Syntax.Ancestors().OfType<CatchClauseSyntax>().First() == Syntax
            );

    /// <summary>The clause header, from <c>catch</c> through its declaration and filter.</summary>
    public SourceLocation Location =>
        Source.Locate(
            TextSpan.FromBounds(
                Syntax.CatchKeyword.SpanStart,
                ((SyntaxNode?)Syntax.Filter ?? Syntax.Declaration)?.Span.End
                    ?? Syntax.CatchKeyword.Span.End
            )
        );
}
