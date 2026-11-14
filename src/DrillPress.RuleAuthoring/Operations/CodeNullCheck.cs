using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A recognized Boolean null test. Nullable facts describe the operand before this test refines its branches.</summary>
public sealed class CodeNullCheck : ICodeElement
{
    private readonly Lazy<NullableFlowState> _beforeCheck;
    private readonly Lazy<NullableOrigin> _origin;

    internal CodeNullCheck(
        CodeExpression condition,
        CodeExpression value,
        NullCheckPolarity polarity,
        NullCheckDomain domain,
        NullCheckForm form
    )
    {
        Condition = condition;
        CheckedValue = value;
        Polarity = polarity;
        Domain = domain;
        Form = form;
        _beforeCheck = new(() => NullableProbe.BeforeCheck(value, condition));
        _origin = new(() => value.NullableOrigins().Origin);
    }

    /// <summary>The underlying comparison, pattern or nullable-presence form.</summary>
    public NullCheckForm Form { get; }

    /// <summary>Whether the checked reference declaration has a non-nullable annotation; this is not a runtime guarantee. A local declared non-nullable can still hold a value from an oblivious declaration; see <see cref="CheckedValueOrigin"/>.</summary>
    public bool HasNonNullableDeclaration =>
        Domain == NullCheckDomain.Reference
        && CheckedValue.DeclaredNullability == NullableAnnotation.NotAnnotated;

    /// <summary>Whether the compiler knows the operand is non-null before this check; independent from its declaration annotation. The compiler also reports values from nullable-oblivious declarations as non-null, so require <see cref="CheckedValueOrigin"/> to be <see cref="NullableOrigin.Aware"/> before treating this as evidence.</summary>
    public bool IsKnownNotNullBeforeCheck => FlowStateBeforeCheck == NullableFlowState.NotNull;

    /// <summary>Whether the checked value comes from declarations with nullable annotations, from <see cref="CodeExpression.NullableOrigins"/>. Oblivious means some source, such as a library compiled without nullable reference types, says nothing about null.</summary>
    public NullableOrigin CheckedValueOrigin => _origin.Value;

    /// <summary>The complete normalized check, including ordinary negation.</summary>
    public CodeExpression Condition { get; }

    /// <summary>The actual checked operand; nullability annotations do not establish runtime invariants.</summary>
    public CodeExpression CheckedValue { get; }

    /// <summary>Which operand state makes the condition true.</summary>
    public NullCheckPolarity Polarity { get; }

    /// <summary>Whether this is a reference-null test or nullable value presence check.</summary>
    public NullCheckDomain Domain { get; }

    /// <summary>The compiler's speculative incoming state before applying this null test; None means unavailable.</summary>
    public NullableFlowState FlowStateBeforeCheck => _beforeCheck.Value;

    /// <summary>The original compiler membership.</summary>
    public AnalysisSource Source => Condition.Source;

    /// <summary>The complete condition span for reporting.</summary>
    public SourceLocation Location => Condition.Location;
}
