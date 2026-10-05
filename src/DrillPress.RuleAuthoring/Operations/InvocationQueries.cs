using DrillPress;
using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Fluent selections over resolved calls, receivers and parameter-associated values.</summary>
public static class InvocationQueries
{
    /// <summary>Selects each resolved call once when any configured declaration parameter has an original source value satisfying the predicate, including extension receivers.</summary>
    public static CodeQuery<CodeInvocation> WhereAnyArgument(
        this CodeQuery<CodeInvocation> calls,
        IReadOnlyList<string> named,
        Func<CodeExpression, bool> value
    )
    {
        var names = ParameterNames(named);
        return calls.Where(call =>
            call.IsResolved
            && call.Arguments.Any(argument =>
                names.Contains(argument.Parameter.Name)
                && argument.Value is { } expression
                && value(expression)
            )
        );
    }

    /// <summary>Projects all values associated with the named declaration roles once, in source evaluation order, including normalized extension receivers and defaults.</summary>
    public static CodeQuery<CodeArgument> ArgumentsFor(
        this CodeQuery<CodeInvocation> calls,
        params string[] parameterNames
    )
    {
        var names = ParameterNames(parameterNames);
        return calls
            .Where(call => call.IsResolved)
            .SelectMany(call =>
                call.Arguments.Where(argument => names.Contains(argument.Parameter.Name))
            );
    }

    private static HashSet<string> ParameterNames(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
            throw new ArgumentException(
                "Select at least one declaration parameter role.",
                nameof(names)
            );
        foreach (var name in names)
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return names.ToHashSet();
    }

    /// <summary>Projects explicit, resolved call expressions with their original source span and compilation membership.</summary>
    public static CodeQuery<CodeExpression> Expressions(this CodeQuery<CodeInvocation> calls) =>
        calls.SelectMany(call => call.Expression is { } expression ? new[] { expression } : []);

    /// <summary>Selects explicit bound calls outside compiler-identified expression trees; the ordinary Calls query retains both contexts.</summary>
    public static CodeQuery<CodeInvocation> OutsideExpressionTrees(
        this CodeQuery<CodeInvocation> calls
    ) =>
        calls.Where(call =>
            call.Expression is { } expression && !expression.Facts.IsInsideExpressionTree
        );

    /// <summary>Selects bound calls declared on exactly the configured owner, including normalized extension declarations.</summary>
    public static CodeQuery<CodeInvocation> ToMethodsDeclaredOn(
        this CodeQuery<CodeInvocation> calls,
        CodeType type
    ) => calls.Where(call => call.IsDeclaredOn(type));

    /// <summary>Selects bound calls whose declaration owner is the configured type or a derived class, independently of receiver type.</summary>
    public static CodeQuery<CodeInvocation> ToMethodsDeclaredOnOrDerivedFrom(
        this CodeQuery<CodeInvocation> calls,
        CodeType type
    ) => calls.Where(call => call.IsDeclaredOnOrDerivedFrom(type));

    /// <summary>Selects bound targets by a case-sensitive name glob; exact-name selections remain available through ToMethodsNamed.</summary>
    public static CodeQuery<CodeInvocation> ToMethodsMatchingName(
        this CodeQuery<CodeInvocation> calls,
        string pattern
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return calls.Where(call => call.TargetNameMatches(pattern));
    }

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

    /// <summary>Projects reportable source values, excluding defaults. Opt into normalized extension receivers to include their original source once in either call spelling.</summary>
    public static CodeQuery<CodeExpression> SourceValues(
        this CodeQuery<CodeArgument> arguments,
        bool includeReceivers = false
    ) =>
        arguments.SelectMany(argument =>
            (argument.IsExplicit || includeReceivers && argument.IsReceiver)
            && argument.Value is { } value
                ? new[] { value }
                : []
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
