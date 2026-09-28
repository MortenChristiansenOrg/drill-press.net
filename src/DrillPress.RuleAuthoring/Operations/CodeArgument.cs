using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Operations;

/// <summary>A source argument value mapped to its declaration parameter; synthesized defaults have no editable location.</summary>
public sealed class CodeArgument
{
    internal CodeArgument(
        AnalysisSource source,
        IParameterSymbol parameter,
        IOperation value,
        ArgumentKind kind,
        int? sourceIndex,
        bool receiver = false
    )
    {
        Source = source;
        Parameter = parameter;
        Operation = value;
        Kind = kind;
        SourceIndex = sourceIndex;
        IsReceiver = receiver;
    }

    /// <summary>The argument's compilation membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The parameter on the normalized extension declaration or ordinary selected method.</summary>
    public IParameterSymbol Parameter { get; }

    /// <summary>The bound value including implicit conversions.</summary>
    public IOperation Operation { get; }

    /// <summary>Whether this value was explicit, defaulted, or supplied through expanded params.</summary>
    public ArgumentKind Kind { get; }

    /// <summary>Position in the source argument list; absent for defaults and reduced extension receivers.</summary>
    public int? SourceIndex { get; }

    /// <summary>Whether this is the extension receiver rather than removable argument syntax.</summary>
    public bool IsReceiver { get; }

    /// <summary>Whether a value was explicitly written, including an expanded params element.</summary>
    public bool IsExplicit => SourceIndex.HasValue;

    /// <summary>The source value, absent for omitted optional arguments or empty synthesized params.</summary>
    public CodeExpression? Value =>
        (IsExplicit || IsReceiver) && Operation.Syntax is ExpressionSyntax expression
            ? new(Source, expression)
            : null;

    /// <summary>The explicit value's span. Callers must choose their own fallback for implicit values.</summary>
    public SourceLocation? Location => Value?.Location;

    /// <summary>The source name-colon, absent for positional, implicit and expanded element values.</summary>
    public string? Name =>
        IsExplicit
            ? Operation
                .Syntax.AncestorsAndSelf()
                .OfType<ArgumentSyntax>()
                .FirstOrDefault()
                ?.NameColon?.Name.Identifier.ValueText
            : null;

    /// <summary>The parameter's bound passing mode; source spelling remains available on its syntax.</summary>
    public RefKind RefKind => Parameter.RefKind;

    /// <summary>The compiler value, including synthesized optional defaults.</summary>
    public Optional<object?> Constant => Operation.ConstantValue;
}
