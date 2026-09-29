using DrillPress.Flow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A recognized Boolean null test. Nullable facts describe the operand before this test refines its branches.</summary>
public sealed class CodeNullCheck : ICodeElement
{
    private readonly Lazy<NullableFlowState> _beforeCheck;

    internal CodeNullCheck(
        CodeExpression condition,
        CodeExpression value,
        NullCheckPolarity polarity,
        NullCheckDomain domain
    )
    {
        Condition = condition;
        CheckedValue = value;
        Polarity = polarity;
        Domain = domain;
        _beforeCheck = new(() => NullableProbe.BeforeCheck(value, condition));
    }

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
