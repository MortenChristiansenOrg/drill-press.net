namespace DrillPress;

/// <summary>Identifies an exact physical source span and its human-readable coordinates.</summary>
/// <param name="FilePath">The physical source file containing the span.</param>
/// <param name="Start">The zero-based character offset of the span.</param>
/// <param name="Length">The span length in characters.</param>
/// <param name="Line">The one-based source line.</param>
/// <param name="Column">The one-based source column.</param>
public sealed record SourceLocation(
    string FilePath,
    int Start,
    int Length,
    int Line,
    int Column);

/// <summary>Describes a source expression bound to a member on a specific CLR type.</summary>
/// <param name="ContainingType">The declaring type resolved by semantic analysis.</param>
/// <param name="MemberName">The metadata name of the referenced member.</param>
/// <param name="Location">The complete source expression to report.</param>
public sealed record MemberReference(
    CodeType ContainingType,
    string MemberName,
    SourceLocation Location);

/// <summary>Defines the stable identifier and remediation text presented for a rule.</summary>
/// <param name="Id">The stable rule identifier.</param>
/// <param name="Message">Concise guidance for correcting a violation.</param>
public sealed record RuleDescriptor(string Id, string Message);

/// <summary>Associates a rule violation with its physical source location.</summary>
/// <param name="Descriptor">The rule that produced the violation.</param>
/// <param name="Location">The violating source expression.</param>
public sealed record RuleDiagnostic(RuleDescriptor Descriptor, SourceLocation Location);
