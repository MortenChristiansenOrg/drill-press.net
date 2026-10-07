using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A written reference to a named type: a declaration type, base type, generic argument, cast, typeof, attribute, static member qualifier or object creation. <c>var</c> is not a reference.</summary>
public sealed class CodeTypeReference : ICodeElement
{
    internal CodeTypeReference(
        AnalysisSource source,
        ExpressionSyntax syntax,
        INamedTypeSymbol type
    )
    {
        Source = source;
        Syntax = syntax;
        Type = type;
    }

    /// <summary>The original document and compilation membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The complete written type name, including any namespace or alias qualification.</summary>
    public ExpressionSyntax Syntax { get; }

    /// <summary>The referenced type, including constructed generic arguments; an attribute reference denotes its attribute class.</summary>
    public INamedTypeSymbol Type { get; }

    /// <summary>Matches the referenced type; open generic descriptors match every construction.</summary>
    public bool RefersTo(CodeType type) => type.Matches(Type);

    /// <summary>Matches the referenced type, including constructed generic arguments.</summary>
    public bool RefersTo<T>() => RefersTo(CodeType.Of<T>());

    /// <summary>Source-context evidence such as nameof membership.</summary>
    public ExpressionSourceFacts Facts => new(Source, Syntax);

    /// <summary>The complete written type name.</summary>
    public SourceLocation Location => Source.Locate(Syntax.Span);
}
