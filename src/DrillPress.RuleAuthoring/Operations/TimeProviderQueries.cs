using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>Recognizes framework waits and timers that run on the system clock instead of a <c>TimeProvider</c>. They never choose which clock an application should use.</summary>
public static class TimeProviderQueries
{
    private static readonly CodeType _timeProvider = CodeType.Of<TimeProvider>();
    private static readonly CodeType _task = CodeType.Of<Task>();
    private static readonly CodeType _timeSpan = CodeType.Of<TimeSpan>();
    private static readonly CodeType _thread = CodeType.Of<Thread>();
    private static readonly CodeType _cancellationSource = CodeType.Of<CancellationTokenSource>();
    private static readonly CodeType _periodicTimer = CodeType.Of<PeriodicTimer>();
    private static readonly CodeType _threadingTimer = CodeType.Of<System.Threading.Timer>();
    private static readonly CodeType _timersTimer = CodeType.Framework("System.Timers.Timer");

    /// <summary>Whether the call waits or times out on the system clock: <c>Task.Delay</c>, a timed <c>WaitAsync</c>, <c>Thread.Sleep</c> or <c>CancelAfter</c>, without a TimeProvider. <c>CancelAfter</c> on a local source created with a TimeProvider uses that one.</summary>
    public static bool WaitsWithoutTimeProvider(this CodeInvocation call)
    {
        var target = call.Target;
        var type = target.ContainingType;
        if (target.Parameters.Any(parameter => IsTimeProvider(parameter.Type)))
            return false;
        return (target.Name == "Delay" && _task.Matches(type))
            || (
                target.Name == "WaitAsync"
                && type.IsTask()
                && target.Parameters.Any(parameter => _timeSpan.Matches(parameter.Type))
            )
            || (target.Name == "Sleep" && _thread.Matches(type))
            || (
                target.Name == "CancelAfter"
                && _cancellationSource.Matches(type)
                && !IsCreatedWithTimeProvider(call.Receiver)
            );
    }

    /// <summary>Whether the creation starts a timer or timeout on the system clock: a <c>PeriodicTimer</c>, a threading or timers <c>Timer</c>, or a <c>CancellationTokenSource</c> with a delay, without a TimeProvider.</summary>
    public static bool TimesWithoutTimeProvider(this CodeObjectCreation creation)
    {
        var parameters = creation.Constructor?.Parameters ?? [];
        if (
            creation.Type is not { } type
            || parameters.Any(parameter => IsTimeProvider(parameter.Type))
        )
            return false;
        return _periodicTimer.Matches(type)
            || _threadingTimer.Matches(type)
            || _timersTimer.Matches(type)
            || (_cancellationSource.Matches(type) && parameters.Length > 0);
    }

    internal static bool IsTimeProvider(ITypeSymbol? type) =>
        type is not null && _timeProvider.Matches(type);

    // A reference to an injected clock: a field, property, parameter or local of type TimeProvider, never TimeProvider.System.
    internal static bool IsInjectedClock(IOperation? operation) =>
        operation.WithoutConversions(includeExplicit: true) switch
        {
            IFieldReferenceOperation { Field.IsStatic: false } field => IsTimeProvider(
                field.Field.Type
            ),
            IPropertyReferenceOperation { Property.IsStatic: false } property => IsTimeProvider(
                property.Property.Type
            ),
            IParameterReferenceOperation parameter => IsTimeProvider(parameter.Parameter.Type),
            ILocalReferenceOperation local => IsTimeProvider(local.Local.Type),
            _ => false,
        };

    // Whether an operation calls the given TimeProvider method on an injected clock.
    internal static bool CallsClock(IOperation? operation, string method) =>
        operation is IInvocationOperation call
        && call.TargetMethod.Name == method
        && IsTimeProvider(call.TargetMethod.ContainingType)
        && IsInjectedClock(call.Instance);

    private static bool IsCreatedWithTimeProvider(CodeExpression? receiver) =>
        receiver?.Symbol is ILocalSymbol local
        && Initializer(local, receiver.Source)?.Operation
            is IObjectCreationOperation { Constructor: { } constructor }
        && constructor.Parameters.Any(parameter => IsTimeProvider(parameter.Type));

    // A local's single written initializer in this syntax tree; later assignments aren't followed.
    private static CodeExpression? Initializer(ILocalSymbol local, AnalysisSource source) =>
        local.DeclaringSyntaxReferences is [var reference]
        && reference.SyntaxTree == source.Tree
        && reference.GetSyntax(source.Project.CancellationToken)
            is VariableDeclaratorSyntax { Initializer.Value: var value }
            ? new(source, value)
            : null;
}
