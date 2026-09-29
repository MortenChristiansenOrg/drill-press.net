using DrillPress;

namespace DrillPress;

/// <summary>The bounded semantic equivalence used to form an extraction candidate.</summary>
public enum ExpressionGroupKind
{
    /// <summary>Equal compiler types and constant values.</summary>
    Constant,

    /// <summary>Equal supported string structure with one typed local/parameter occurrence substituted.</summary>
    OneHoleTemplate,
}

/// <summary>Explicitly supported string template syntax; no algebraic equivalence is inferred between forms.</summary>
[Flags]
public enum TemplateShapes
{
    /// <summary>Ordinary string interpolation with one interpolation clause.</summary>
    Interpolation = 1,

    /// <summary>Built-in string concatenation with fixed constant portions.</summary>
    Concatenation = 2,
}

/// <summary>A selected occurrence and its optional, explicitly recognized hole.</summary>
/// <param name="Expression">The entire original source expression and semantic evidence.</param>
/// <param name="Capture">The one local/parameter occurrence for a template; absent for constants.</param>
public sealed record ExpressionOccurrence(CodeExpression Expression, CodeExpression? Capture);
