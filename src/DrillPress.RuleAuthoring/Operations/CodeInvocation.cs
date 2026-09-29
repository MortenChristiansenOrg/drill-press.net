using DrillPress.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>A bound call site with overload identity and compiler-mapped arguments.</summary>
public sealed class CodeInvocation(AnalysisSource source, IInvocationOperation operation)
    : ICodeElement
{
    private IReadOnlyList<CodeArgument>? _arguments;
    private bool? _resolved;

    /// <summary>The call's original source context.</summary>
    public AnalysisSource Source { get; } = source;

    /// <summary>The resolved invocation, including receiver and implicit argument values.</summary>
    public IInvocationOperation Operation { get; } = operation;

    /// <summary>The selected overload.</summary>
    public IMethodSymbol Target => Operation.TargetMethod;

    /// <summary>The extension declaration, retaining constructed type arguments where Roslyn supplies them.</summary>
    public IMethodSymbol Declaration =>
        Target.ReducedFrom is { } definition
            ? Target.Arity == 0
                ? definition
                : definition.Construct(Target.TypeArguments.ToArray())
            : Target;

    /// <summary>Whether this is an instance, extension or ordinary static call.</summary>
    public ReceiverKind ReceiverKind =>
        Declaration.IsExtensionMethod ? ReceiverKind.Extension
        : Operation.Instance is not null ? ReceiverKind.Instance
        : ReceiverKind.None;

    /// <summary>Whether this invocation belongs to the conditional arm of conditional access.</summary>
    public bool IsConditional =>
        Operation
            .Syntax.AncestorsAndSelf()
            .Any(node =>
                node is ConditionalAccessExpressionSyntax conditional
                && conditional.WhenNotNull.Span.Contains(Operation.Syntax.Span)
            );

    /// <summary>Reads a parameter group, including an empty expanded params group. Unknown parameter names return null.</summary>
    public ParameterArguments? Parameter(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        return
            Declaration.Parameters.FirstOrDefault(parameter => parameter.Name == parameterName)
                is { } parameter
            ? new(parameter, Array.AsReadOnly(ArgumentsFor(parameterName).ToArray()))
            : null;
    }

    /// <summary>Receiver and explicit input values in evaluation order, without duplicating an extension receiver or inventing default evaluations.</summary>
    public IEnumerable<CallInput> EvaluationInputs()
    {
        if (ReceiverKind == ReceiverKind.Instance && Receiver is { } receiver)
            yield return new(receiver, null, true);
        foreach (var argument in Arguments)
            if (argument.Value is { } value)
                yield return new(value, argument.Parameter, argument.IsReceiver);
    }

    /// <summary>Whether this call binds without compiler errors; invalid child arguments do not count as resolved calls.</summary>
    public bool IsResolved =>
        _resolved ??=
            Target.ContainingType.TypeKind != TypeKind.Error
            && !Source
                .Model.GetDiagnostics(Operation.Syntax.Span, Source.Project.CancellationToken)
                .Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>The actual instance or extension receiver before contextual conversion; absent for ordinary static calls.</summary>
    public CodeExpression? Receiver
    {
        get
        {
            var value =
                Operation.Instance
                ?? (
                    Declaration.IsExtensionMethod
                        ? Operation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0)?.Value
                        : null
                );
            if (value is IConditionalAccessInstanceOperation)
            {
                for (var parent = Operation.Parent; parent is not null; parent = parent.Parent)
                    if (parent is IConditionalAccessOperation conditional)
                    {
                        value = conditional.Operation;
                        break;
                    }
            }
            return value is IInstanceReferenceOperation { IsImplicit: true }
                && Operation.Syntax is ExpressionSyntax anchor
                    ? CodeExpression.ImplicitReceiver(Source, anchor, value)
                : value?.Syntax is ExpressionSyntax expression ? new(Source, expression)
                : null;
        }
    }

    /// <summary>Parameter-associated values in source evaluation order. Defaults follow explicit values and have no source index.</summary>
    public IReadOnlyList<CodeArgument> Arguments =>
        _arguments ??= Array.AsReadOnly(
            ReadArguments()
                .OrderBy(a =>
                    a.IsReceiver && a.SourceIndex is null ? -1 : a.SourceIndex ?? int.MaxValue
                )
                .ToArray()
        );

    /// <summary>All supplied values for a declaration parameter, including expanded params elements and implicit defaults.</summary>
    public IEnumerable<CodeArgument> ArgumentsFor(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        return Arguments.Where(a => a.Parameter.Name == parameterName);
    }

    private IEnumerable<CodeArgument> ReadArguments()
    {
        var syntax = Operation.Syntax as InvocationExpressionSyntax;
        foreach (var argument in Operation.Arguments)
        {
            if (argument.Parameter is not { } parameter)
                continue;
            if (Target.ReducedFrom is not null)
                parameter = Declaration.Parameters[parameter.Ordinal + 1];
            if (
                argument.ArgumentKind == ArgumentKind.ParamArray
                && argument.Value is IArrayCreationOperation { Initializer: { } initializer }
            )
            {
                foreach (var element in initializer.ElementValues)
                    yield return new(
                        this,
                        parameter,
                        element,
                        argument.ArgumentKind,
                        Index(element.Syntax)
                    );
            }
            else if (
                argument.ArgumentKind == ArgumentKind.ParamCollection
                && argument.Value is ICollectionExpressionOperation collection
            )
            {
                foreach (var element in collection.Elements)
                    yield return new(
                        this,
                        parameter,
                        element,
                        argument.ArgumentKind,
                        Index(element.Syntax)
                    );
            }
            else
                yield return new(
                    this,
                    parameter,
                    argument.Value,
                    argument.ArgumentKind,
                    argument.ArgumentKind == ArgumentKind.DefaultValue
                        ? null
                        : Index(argument.Syntax),
                    Declaration.IsExtensionMethod && parameter.Ordinal == 0
                );
        }
        if (Target.ReducedFrom is not null && Operation.Instance is { } receiver)
            yield return new(
                this,
                Declaration.Parameters[0],
                receiver,
                ArgumentKind.Explicit,
                null,
                true
            );

        int? Index(SyntaxNode node)
        {
            if (syntax is null)
                return null;
            for (var i = 0; i < syntax.ArgumentList.Arguments.Count; i++)
                if (syntax.ArgumentList.Arguments[i].Span.Contains(node.Span))
                    return i;
            return null;
        }
    }

    /// <summary>The complete call span.</summary>
    public SourceLocation Location => Source.Locate(Operation.Syntax.Span);

    /// <summary>Matches a configured API independently of aliases, static imports or extension-call spelling.</summary>
    public bool Calls(CodeMember member) => member.Matches(Target);

    /// <summary>Gets the bound argument for a declared parameter, including optional defaults. Returns null when absent.</summary>
    public IArgumentOperation? Argument(string parameterName) =>
        Operation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == parameterName);
}
