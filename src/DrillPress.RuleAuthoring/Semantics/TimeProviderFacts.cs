using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static DrillPress.AsyncFacts;

namespace DrillPress;

/// <summary>Framework time APIs and explicit TimeProvider usage; never chooses an application's clock.</summary>
public static class TimeProviderFacts
{
    /// <summary>Recognizes System.TimeProvider by original declaration name and namespace.</summary>
    public static bool IsTimeProvider(ITypeSymbol? type) => IsType(type, "System", "TimeProvider");

    private static bool IsCancellationToken(ITypeSymbol? type) =>
        IsType(type, "System.Threading", "CancellationToken");

    /// <summary>A reference to an injected clock: a field, property, parameter or local of type TimeProvider, never TimeProvider.System.</summary>
    public static bool IsInjectedClock(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }
        return operation switch
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
    }

    /// <summary>Whether an operation calls the given TimeProvider method on an injected clock.</summary>
    public static bool CallsClock(IOperation? operation, string method) =>
        operation is IInvocationOperation call
        && call.TargetMethod.Name == method
        && IsTimeProvider(call.TargetMethod.ContainingType)
        && IsInjectedClock(call.Instance);

    /// <summary>Recognizes Task.Delay, timed Task.WaitAsync, Thread.Sleep and CancellationTokenSource.CancelAfter without a TimeProvider, accounting for a local source initialized with one.</summary>
    public static bool WaitsWithoutClock(CodeInvocation call)
    {
        var target = call.Target;
        var type = target.ContainingType;
        if (target.Parameters.Any(parameter => IsTimeProvider(parameter.Type)))
        {
            return false;
        }
        return (target.Name == "Delay" && IsType(type, "System.Threading.Tasks", "Task"))
            || (
                target.Name == "WaitAsync"
                && IsTask(type)
                && target.Parameters.Any(parameter => IsType(parameter.Type, "System", "TimeSpan"))
            )
            || (target.Name == "Sleep" && IsType(type, "System.Threading", "Thread"))
            || (
                target.Name == "CancelAfter"
                && IsType(type, "System.Threading", "CancellationTokenSource")
                && !IsCreatedWithClock(call.Receiver)
            );
    }

    private static bool IsCreatedWithClock(CodeExpression? receiver) =>
        receiver?.Symbol is ILocalSymbol local
        && local.Initializer(receiver.Source)?.Operation
            is IObjectCreationOperation { Constructor: { } constructor }
        && constructor.Parameters.Any(parameter => IsTimeProvider(parameter.Type));

    /// <summary>Recognizes PeriodicTimer, threading/timers Timer and timed CancellationTokenSource construction without a TimeProvider parameter.</summary>
    public static bool TimesWithoutClock(CodeObjectCreation creation)
    {
        var type = creation.Type;
        var parameters = creation.Constructor?.Parameters ?? [];
        if (parameters.Any(parameter => IsTimeProvider(parameter.Type)))
        {
            return false;
        }
        return IsType(type, "System.Threading", "PeriodicTimer")
            || IsType(type, "System.Threading", "Timer")
            || IsType(type, "System.Timers", "Timer")
            || (
                IsType(type, "System.Threading", "CancellationTokenSource") && parameters.Length > 0
            );
    }
}
