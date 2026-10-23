namespace DrillPress;

/// <summary>The code whose execution coverage describes an element; it differs from where the element reports.</summary>
internal static class CoverageSpan
{
    // Calls report at their method name but execute as a whole, and an omitted argument executes with its call.
    internal static SourceLocation Of(ICodeElement element) =>
        element switch
        {
            CodeInvocation call => call.Source.Locate(call.Operation.Syntax.Span),
            CodeArgument { Location: null } argument => Of(argument.Invocation),
            _ => element.Location,
        };
}
