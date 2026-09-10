namespace DrillPress.Testing;

/// <summary>One synthetic source membership. Use the same path and text across projects to exercise linked-file agreement.</summary>
/// <param name="Path">A stable physical-style path; no source file is written.</param>
/// <param name="Text">Exact source, including whitespace used by location assertions.</param>
/// <param name="Generated">Whether the source supplies semantics without reportable candidates or edits.</param>
public sealed record TestSource(string Path, string Text, bool Generated = false);

/// <summary>A stable consumer assertion value without internal response identifiers.</summary>
/// <param name="Rule">Rule identifier.</param>
/// <param name="Path">Reported source path.</param>
/// <param name="Line">One-based physical line.</param>
/// <param name="Column">One-based UTF-16 column.</param>
/// <param name="Text">Exact reported source span.</param>
/// <param name="HasFix">Whether the full proposal survived context and conflict validation.</param>
public sealed record TestFinding(string Rule, string Path, int Line, int Column, string Text, bool HasFix);
