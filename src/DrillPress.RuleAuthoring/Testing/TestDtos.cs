namespace DrillPress.Testing;

/// <summary>A resolved xUnit assertion in a test's immediate body.</summary>
/// <param name="MemberName">The resolved assertion API name.</param>
/// <param name="Location">The complete invocation span.</param>
public sealed record TestAssertion(string MemberName, SourceLocation Location);
