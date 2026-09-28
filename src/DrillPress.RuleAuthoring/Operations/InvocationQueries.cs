using DrillPress.Configuration;
using Microsoft.CodeAnalysis;

namespace DrillPress.Operations;

/// <summary>Fluent selections over resolved calls, receivers and parameter-associated values.</summary>
public static class InvocationQueries
{
    /// <summary>Projects reportable explicit value expressions, excluding defaults and synthesized receivers without source argument syntax.</summary>
    public static CodeQuery<CodeExpression> SourceValues(this CodeQuery<CodeArgument> arguments) =>
        arguments.SelectMany(argument =>
            argument.IsExplicit && argument.Value is { } value ? new[] { value } : []
        );

    /// <summary>Matches a configured method without discarding its actual overload.</summary>
    public static CodeQuery<CodeInvocation> Calling(
        this CodeQuery<CodeInvocation> calls,
        CodeMember member
    ) => calls.Where(call => call.IsResolved && call.Calls(member));

    /// <summary>Matches any configured member family or overload.</summary>
    public static CodeQuery<CodeInvocation> Calling(
        this CodeQuery<CodeInvocation> calls,
        ApiSet members
    ) => calls.Where(call => call.IsResolved && members.Contains(call.Target));

    /// <summary>Matches an instance or extension receiver; ordinary static calls do not match.</summary>
    public static CodeQuery<CodeInvocation> WhereReceiver(
        this CodeQuery<CodeInvocation> calls,
        Func<CodeExpression, bool> predicate
    ) =>
        calls.Where(call =>
            call.IsResolved && call.Receiver is { } receiver && predicate(receiver)
        );

    /// <summary>Matches any value associated with the named declaration parameter, including optional defaults.</summary>
    public static CodeQuery<CodeInvocation> WhereArgument(
        this CodeQuery<CodeInvocation> calls,
        string parameterName,
        Func<CodeArgument, bool> predicate
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        return calls.Where(call =>
            call.IsResolved && call.ArgumentsFor(parameterName).Any(predicate)
        );
    }

    /// <summary>Projects parameter groups without inventing source locations for default arguments.</summary>
    public static CodeQuery<CodeArgument> ArgumentsFor(
        this CodeQuery<CodeInvocation> calls,
        string parameterName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        return calls.SelectMany(call => call.ArgumentsFor(parameterName));
    }
}
