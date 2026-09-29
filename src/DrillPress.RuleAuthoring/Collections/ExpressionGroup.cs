using DrillPress;

namespace DrillPress.Collections;

/// <summary>A stable set of selected semantic occurrences within one source type definition and evaluated context. Grouping does not authorize a fix.</summary>
public sealed class ExpressionGroup : ICodeElement
{
    internal ExpressionGroup(
        CodeDeclaration owner,
        ExpressionGroupKind kind,
        IReadOnlyList<ExpressionOccurrence> occurrences,
        OneHoleTemplateOptions? options
    )
    {
        Owner = owner;
        Kind = kind;
        Occurrences = occurrences;
        Options = options;
    }

    /// <summary>The deterministic ordinary partial declaration of the containing type; nested types and other contexts remain separate.</summary>
    public CodeDeclaration Owner { get; }

    /// <summary>The kind of bounded candidate equivalence.</summary>
    public ExpressionGroupKind Kind { get; }

    /// <summary>Unique selected occurrences in stable physical source order, including their individual capture mappings.</summary>
    public IReadOnlyList<ExpressionOccurrence> Occurrences { get; }

    /// <summary>The first selected expression; no unselected source occurrence is discovered implicitly.</summary>
    public CodeExpression Representative => Occurrences[0].Expression;

    /// <summary>The representative's original source membership.</summary>
    public AnalysisSource Source => Representative.Source;

    /// <summary>The representative expression's diagnostic span.</summary>
    public SourceLocation Location => Representative.Location;

    /// <summary>Tests source-occurrence identity within the same evaluated compilation.</summary>
    public bool Contains(CodeExpression expression) =>
        Occurrences.Any(occurrence =>
            occurrence.Expression.Source == expression.Source
            && occurrence.Expression.Syntax.Span == expression.Syntax.Span
        );

    internal OneHoleTemplateOptions? Options { get; }
}
