using DrillPress;
using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Fluent selections over resolved calls, receivers and parameter-associated values.</summary>
public static class InvocationQueries
{
    /// <summary>Selects calls by ordinal method name without assuming their declaring type.</summary>
    public static CodeQuery<CodeInvocation> ToMethodsNamed(
        this CodeQuery<CodeInvocation> calls,
        params string[] names
    )
    {
        foreach (var name in names)
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var selected = names.ToHashSet();
        return calls.Where(call => call.IsResolved && selected.Contains(call.Target.Name));
    }

    /// <summary>Selects instance and normalized extension receivers of a configured type or derived class.</summary>
    public static CodeQuery<CodeInvocation> OnReceiverOfType(
        this CodeQuery<CodeInvocation> calls,
        CodeType type
    ) => calls.WhereReceiver(receiver => receiver.TypeIsOrDerivesFrom(type));

    /// <summary>Selects instance and extension calls on the specified runtime type or its derived classes.</summary>
    public static CodeQuery<CodeInvocation> OnReceiverOfType<T>(
        this CodeQuery<CodeInvocation> calls
    ) => calls.OnReceiverOfType(CodeType.Of<T>());

    /// <summary>Projects source arguments whose bound parameter has one of the given types and, if supplied, names. Multiple matches remain separate.</summary>
    public static CodeQuery<CodeArgument> ArgumentsOfTypes(
        this CodeQuery<CodeInvocation> calls,
        IReadOnlyList<CodeType> types,
        params string[] named
    )
    {
        var selected = types.ToArray();
        foreach (var name in named)
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var names = named.ToHashSet();
        return calls
            .Where(call => call.IsResolved)
            .SelectMany(call =>
                call.Arguments.Where(argument =>
                    argument.IsExplicit
                    && !argument.IsReceiver
                    && (names.Count == 0 || names.Contains(argument.Parameter.Name))
                    && selected.Any(type => type.Matches(argument.Parameter.Type))
                )
            );
    }

    /// <summary>Matches a family member with no such parameter, or a present parameter whose effective value satisfies the predicate. Missing parameters and omitted defaults remain distinct to the predicate.</summary>
    public static CodeQuery<CodeInvocation> WhereArgumentOrMissing(
        this CodeQuery<CodeInvocation> calls,
        string parameter,
        Func<CodeArgument, bool> predicate
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        return calls.Where(call =>
            call.IsResolved
            && (call.Parameter(parameter) is null || call.ArgumentsFor(parameter).Any(predicate))
        );
    }

    /// <summary>Selects arguments passed to a configured method family.</summary>
    public static CodeQuery<CodeArgument> To(
        this CodeQuery<CodeArgument> arguments,
        CodeMember member
    ) =>
        arguments.Where(argument =>
            argument.Invocation.IsResolved && argument.Invocation.Calls(member)
        );

    /// <summary>Selects arguments passed to any configured overload.</summary>
    public static CodeQuery<CodeArgument> To(
        this CodeQuery<CodeArgument> arguments,
        ApiSet members
    ) =>
        arguments.Where(argument =>
            argument.Invocation.IsResolved && members.Contains(argument.Invocation.Target)
        );

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
