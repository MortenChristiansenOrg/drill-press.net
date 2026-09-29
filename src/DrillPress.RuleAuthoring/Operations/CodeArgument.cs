using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>A source argument value mapped to its declaration parameter; synthesized defaults have no editable location.</summary>
public sealed class CodeArgument : ICodeElement
{
    internal CodeArgument(
        CodeInvocation invocation,
        IParameterSymbol parameter,
        IOperation value,
        ArgumentKind kind,
        int? sourceIndex,
        bool receiver = false
    )
    {
        Invocation = invocation;
        Parameter = parameter;
        Operation = value;
        Kind = kind;
        SourceIndex = sourceIndex;
        IsReceiver = receiver;
    }

    /// <summary>The argument's compilation membership.</summary>
    public AnalysisSource Source => Invocation.Source;

    /// <summary>The call containing this parameter-associated value.</summary>
    public CodeInvocation Invocation { get; }

    /// <summary>The compile-time string value, or null when unavailable.</summary>
    public string? TextValue => ValueAs<string>() is { HasValue: true } value ? value.Value : null;

    /// <summary>Reads a compiler constant, including optional defaults, with exact enum identity.</summary>
    public Optional<T> ValueAs<T>() => CompilerConstant.Read<T>(Constant, Operation.Type);

    /// <summary>Matches the effective typed constant, including compiler-supplied optional defaults.</summary>
    public bool Is<T>(T value) =>
        ValueAs<T>() is { HasValue: true } actual
        && EqualityComparer<T>.Default.Equals(actual.Value, value);

    /// <summary>Matches an omitted optional argument or an explicitly supplied typed value. This does not assert equivalence to the optional default.</summary>
    public bool IsOmittedOr<T>(T value) => Kind == ArgumentKind.DefaultValue || Is(value);

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

    SourceLocation ICodeElement.Location =>
        Location
        ?? throw new InvalidOperationException(
            "An implicit argument requires an explicit reporting location."
        );

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
