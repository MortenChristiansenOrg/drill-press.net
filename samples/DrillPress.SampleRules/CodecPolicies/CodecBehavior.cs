using DrillPress.Configuration;
using DrillPress.Operations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.SampleRules.CodecPolicies;

// Compiler-backed behavior predicates, separate from the policy declarations and their messages.
internal static class CodecBehavior
{
    private static readonly ApiSet _nonRepeatableValues = new(
        CodeType.Of<Guid>().Member(nameof(Guid.NewGuid)),
        CodeType.Of<Random>().Member(nameof(Random.Next))
    );
    private static readonly CodeMember _consoleWriteLine = CodeType
        .Named("System.Console")
        .Member("WriteLine");
    private static readonly CodeMember _blockingSleep = CodeType
        .Named("System.Threading.Thread")
        .Member("Sleep");
    private static readonly CodeMember _trimWithoutArguments = CodeType
        .Of<string>()
        .Member(nameof(string.Trim))
        .WithParameters();
    private static readonly CodeMember _rentBuffer = CodeType
        .Named("System.Buffers.ArrayPool<>")
        .Member("Rent");
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
    ) => methods.Where(method => method.Reaches(_blockingSleep));

    internal static bool CapturesRentedBuffer(CodeMethod method) =>
        method.Flow.Data is { Succeeded: true } data
        && data.Captured.OfType<ILocalSymbol>().Any(local => IsRentedBuffer(method, local));

    // This ownership policy bans captures even when a callback happens to run before Return.
    // It recognizes direct Rent initializers, not aliases or interprocedural buffer provenance.
    private static bool IsRentedBuffer(CodeMethod method, ILocalSymbol local) =>
        local.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(method.Source.Project.CancellationToken)
                is VariableDeclaratorSyntax { Initializer.Value: { } value }
            && method.Source.Model.GetOperation(value, method.Source.Project.CancellationToken)
                is IInvocationOperation call
            && _rentBuffer.Matches(call.TargetMethod)
        );
}
