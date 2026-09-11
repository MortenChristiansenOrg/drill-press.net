using DrillPress.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Operations;

/// <summary>A bound call site with overload identity and compiler-mapped arguments.</summary>
public sealed class CodeInvocation(AnalysisSource source, IInvocationOperation operation)
    : ICodeElement
{
    /// <summary>The call's original source context.</summary>
    public AnalysisSource Source { get; } = source;

    /// <summary>The resolved invocation, including receiver and implicit argument values.</summary>
    public IInvocationOperation Operation { get; } = operation;

    /// <summary>The selected overload.</summary>
    public IMethodSymbol Target => Operation.TargetMethod;

    /// <summary>The complete call span.</summary>
    public SourceLocation Location => Source.Locate(Operation.Syntax.Span);

    /// <summary>Matches a configured API independently of aliases, static imports or extension-call spelling.</summary>
    public bool Calls(CodeMember member) => member.Matches(Target);

    /// <summary>Gets the bound argument for a declared parameter, including optional defaults. Returns null when absent.</summary>
    public IArgumentOperation? Argument(string parameterName) =>
        Operation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == parameterName);
}
