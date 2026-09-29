namespace DrillPress.Operations;

/// <summary>The source origin of a string-building segment.</summary>
public enum BuiltStringPartKind
{
    /// <summary>Text written in a literal or interpolation text token.</summary>
    Literal,

    /// <summary>An evaluated expression, retained even when the compiler knows its value.</summary>
    Hole,
}

/// <summary>One ordered source segment; hole formatting is evidence, not an instruction to evaluate it.</summary>
/// <param name="Kind">Whether this is literal text or an expression hole.</param>
/// <param name="Text">Decoded text for literals, otherwise null.</param>
/// <param name="Value">The original expression for a hole, otherwise null.</param>
/// <param name="Location">The original segment's physical location.</param>
/// <param name="Alignment">The original interpolation alignment expression, if present.</param>
/// <param name="Format">The interpolation format text, if present.</param>
public sealed record BuiltStringPart(
    BuiltStringPartKind Kind,
    string? Text,
    CodeExpression? Value,
    SourceLocation Location,
    CodeExpression? Alignment = null,
    string? Format = null
);
