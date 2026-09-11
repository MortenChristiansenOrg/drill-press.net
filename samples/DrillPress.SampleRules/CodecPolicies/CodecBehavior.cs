using DrillPress.Configuration;
using DrillPress.Flow;
using DrillPress.Operations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.SampleRules.CodecPolicies;

// Compiler-backed behavior predicates, separate from the policy declarations and their messages.
internal static class CodecBehavior
{
    private static readonly ApiSet _nonRepeatableValues = new(
        new(CodeType.Of<Guid>(), nameof(Guid.NewGuid)),
        new(CodeType.Of<Random>(), nameof(Random.Next))
    );
    private static readonly CodeMember _consoleWriteLine = new(
        CodeType.Named("System.Console"),
        "WriteLine"
    );
    private static readonly CodeMember _blockingSleep = new(
        CodeType.Named("System.Threading.Thread"),
        "Sleep"
    );
    private static readonly CodeMember _trimWithoutArguments = new(
        CodeType.Of<string>(),
        nameof(string.Trim),
        []
    );
    private static readonly PathPattern _tracingFiles = new("**/Tracing/*.cs");

    internal static bool CreatesNonRepeatableValues(CodeInvocation call) =>
        _nonRepeatableValues.Contains(call.Target);

    internal static bool WritesToConsole(CodeInvocation call) => call.Calls(_consoleWriteLine);

    internal static bool IsInTracingAdapter(CodeInvocation call) =>
        _tracingFiles.Matches(call.Source.Document.Path);

    internal static bool TrimsPossiblyNullText(CodeInvocation call) =>
        call.Calls(_trimWithoutArguments)
        && call.Operation.Instance is { } receiver
        && call.Source.Model.GetTypeInfo(
            receiver.Syntax,
            call.Source.Project.CancellationToken
        ).Nullability.FlowState == NullableFlowState.MaybeNull;

    internal static CodeQuery<CodeMethod> WhoseCallPathsReachBlockingSleep(
        this CodeQuery<CodeMethod> methods
    ) =>
        CodeQuery<CodeMethod>.Create(solution =>
            methods
                .In(solution)
                .Where(method =>
                    method.Symbol is { } symbol
                    && CodeRelationships.In(solution).Reaches(symbol, _blockingSleep)
                )
        );

    internal static CodeQuery<CodeMethod> ThatCapture(
        this CodeQuery<CodeMethod> methods,
        string variableName
    ) =>
        CodeQuery<CodeMethod>.Create(solution =>
            methods
                .In(solution)
                .Where(method =>
                    MethodFlow.For(solution, method).Data is { Succeeded: true } data
                    && data.Captured.Any(symbol => symbol.Name == variableName)
                )
        );
}
