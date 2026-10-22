using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

// Follows a call's receiver and the call that receives its result, in instance, extension and conditional-access spelling.
internal static class CallChainLinks
{
    /// <summary>The value a call works on: its instance or an extension method's first argument, before implicit conversions.</summary>
    internal static IOperation? Receiver(IInvocationOperation call)
    {
        var value = Unconverted(
            call.Instance
                ?? (
                    call.TargetMethod.IsExtensionMethod
                        ? call
                            .Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)
                            ?.Value
                        : null
                )
        );
        return value is IConditionalAccessInstanceOperation instance
            ? Unconverted(AccessOf(instance)?.Operation)
            : value;
    }

    /// <summary>The call that uses this call's result as its receiver, if any.</summary>
    internal static IInvocationOperation? Consumer(IInvocationOperation call)
    {
        var value = Converted(call);
        return value.Parent is IConditionalAccessOperation access && access.Operation == value
            ? access
                .WhenNotNull.DescendantsAndSelf()
                .OfType<IConditionalAccessInstanceOperation>()
                .Where(instance => AccessOf(instance) == access)
                .Select(instance => ReceivedBy(Converted(instance)))
                .FirstOrDefault(consumer => consumer is not null)
            : ReceivedBy(value);
    }

    internal static IOperation? Unconverted(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;
        return operation;
    }

    private static IOperation Converted(IOperation operation)
    {
        while (operation.Parent is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion;
        return operation;
    }

    private static IInvocationOperation? ReceivedBy(IOperation value) =>
        value.Parent switch
        {
            IInvocationOperation parent when parent.Instance == value => parent,
            IArgumentOperation { Parent: IInvocationOperation parent } argument
                when parent.TargetMethod.IsExtensionMethod && argument.Parameter?.Ordinal == 0 =>
                parent,
            _ => null,
        };

    // The conditional access whose tested value an instance placeholder stands for: the one whose
    // WhenNotNull branch contains it.
    internal static IConditionalAccessOperation? AccessOf(
        IConditionalAccessInstanceOperation instance
    )
    {
        IOperation child = instance;
        for (
            var parent = instance.Parent;
            parent is not null;
            child = parent, parent = parent.Parent
        )
            if (parent is IConditionalAccessOperation access && access.WhenNotNull == child)
                return access;
        return null;
    }
}
