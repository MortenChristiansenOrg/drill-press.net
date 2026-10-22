using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static DrillPress.AsyncFacts;
using static DrillPress.TimeProviderFacts;

namespace DrillPress;

/// <summary>Replaces supported framework clock reads, elapsed-time measurements and timed waits with a caller-selected TimeProvider in scope.</summary>
/// <remarks>Local date kinds are not changed. Stopwatch measurements require a timestamp local used only by GetElapsedTime in the same function. Named/default timed-wait shapes not supported by the proof are withheld.</remarks>
public sealed class TimeProviderReplacement
{
    private readonly AnalysisSource _source;
    private readonly ExpressionSyntax _syntax;
    private readonly string _clock;

    internal TimeProviderReplacement(AnalysisSource source, ExpressionSyntax syntax, string clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clock);
        _source = source;
        _syntax = syntax;
        _clock = clock;
    }

    /// <summary>Proposes the supported replacement, proving the selected clock is a TimeProvider local, parameter or instance member and retained inputs still bind. The rule is responsible for selecting the application's intended clock.</summary>
    public FixProposal? Propose() =>
        _source.Model.GetOperation(_syntax) switch
        {
            IPropertyReferenceOperation property
                when CodeType.Of<DateTime>().Matches(property.Property.ContainingType)
                    || CodeType.Of<DateTimeOffset>().Matches(property.Property.ContainingType) =>
                ReadInjectedClock(new(_source, _syntax), _clock),
            IInvocationOperation call
                when IsType(call.TargetMethod.ContainingType, "System.Diagnostics", "Stopwatch") =>
                MeasureWithClock(new(_source, call), _clock),
            IInvocationOperation call
                when TimeProviderFacts.WaitsWithoutClock(new(_source, call)) => WaitWithClock(
                new(_source, call),
                _clock
            ),
            IObjectCreationOperation when _syntax is BaseObjectCreationExpressionSyntax creation =>
                TimeWithClock(new(_source, creation), _clock),
            _ => null,
        };

    private static bool IsCancellationToken(ITypeSymbol? type) =>
        IsType(type, "System.Threading", "CancellationToken");

    private static FixProposal? ReadInjectedClock(CodeExpression reference, string clock)
    {
        var (template, proof) = (
            reference.Symbol!.ContainingType.Name,
            reference.Symbol.Name
        ) switch
        {
            (nameof(DateTimeOffset), nameof(DateTimeOffset.UtcNow)) => (
                "{c}.GetUtcNow()",
                (Func<IOperation?, bool>)(
                    after => CallsClock(after, nameof(TimeProvider.GetUtcNow))
                )
            ),
            (nameof(DateTimeOffset), nameof(DateTimeOffset.Now)) => (
                "{c}.GetLocalNow()",
                after => CallsClock(after, nameof(TimeProvider.GetLocalNow))
            ),
            (nameof(DateTime), nameof(DateTime.UtcNow)) => (
                "{c}.GetUtcNow().UtcDateTime",
                after =>
                    after
                        is IPropertyReferenceOperation
                        {
                            Property.Name: nameof(DateTimeOffset.UtcDateTime)
                        } property
                    && CallsClock(property.Instance, nameof(TimeProvider.GetUtcNow))
            ),
            // DateTime.Now and Today have Local kind, which no TimeProvider expression reproduces exactly.
            _ => (null, null),
        };
        if (template is null || proof is null)
        {
            return null;
        }
        return Fix.For(reference)
            .ReplaceWith(template.Replace("{c}", clock, StringComparison.Ordinal))
            .SafeWhen(change =>
                proof(change.After.Operation) && change.Same(change.Before.Type, change.After.Type)
            );
    }

    // Stopwatch.GetTimestamp() and Stopwatch.GetElapsedTime(started) are rewritten together: only a timestamp stored in a
    // local that is read solely by GetElapsedTime in the same function, so both sides always use the same clock.
    private static FixProposal? MeasureWithClock(CodeInvocation call, string clock)
    {
        var model = call.Source.Model;
        var syntax = call.Operation.Syntax;
        if (call.Target.Name == "GetTimestamp" && call.Arguments.Count == 0)
        {
            if (
                syntax.Parent
                    is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
                || model.GetDeclaredSymbol(declarator) is not ILocalSymbol local
                || !IsOnlyMeasured(local, model)
            )
            {
                return null;
            }
            return Fix.For(call)
                .ReplaceWith($"{clock}.GetTimestamp()")
                .SafeWhen(change => CallsClock(change.After.Operation, "GetTimestamp"));
        }
        if (
            call.Target.Name == "GetElapsedTime"
            && call.Arguments is [{ Value: { } started }]
            && started.Symbol is ILocalSymbol measured
            && IsOnlyMeasured(measured, model)
        )
        {
            return Fix.For(call)
                .ReplaceWith($"{clock}.GetElapsedTime({{0}})", started)
                .SafeWhen(change => CallsClock(change.After.Operation, "GetElapsedTime"));
        }
        return null;
    }

    private static bool IsOnlyMeasured(ILocalSymbol local, SemanticModel model)
    {
        if (
            local.DeclaringSyntaxReferences is not [var reference]
            || reference.GetSyntax()
                is not VariableDeclaratorSyntax { Initializer.Value: var initializer } declarator
            || model.GetOperation(initializer)
                is not IInvocationOperation { TargetMethod.Name: "GetTimestamp" } timestamp
            || !IsType(timestamp.TargetMethod.ContainingType, "System.Diagnostics", "Stopwatch")
        )
        {
            return false;
        }
        if (declarator.Ancestors().FirstOrDefault(IsFunctionBoundary) is not { } function)
            return false;
        return function
            .DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(name =>
                SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, local)
            )
            .All(name =>
                name.Parent
                    is ArgumentSyntax
                    {
                        Parent: ArgumentListSyntax
                        {
                            Arguments.Count: 1,
                            Parent: InvocationExpressionSyntax use
                        }
                    }
                && model.GetSymbolInfo(use).Symbol
                    is IMethodSymbol { Name: "GetElapsedTime" } elapsed
                && IsType(elapsed.ContainingType, "System.Diagnostics", "Stopwatch")
            );
    }

    private static FixProposal? WaitWithClock(CodeInvocation call, string clock)
    {
        var syntax = (InvocationExpressionSyntax)call.Operation.Syntax;
        if (
            call.Arguments.Any(argument => !argument.IsExplicit && !argument.IsReceiver)
            || call.Arguments.Where(argument => !argument.IsReceiver).FirstOrDefault()
                is not { Value: { } delay } first
            || !IsType(first.Parameter.Type, "System", "TimeSpan")
        )
        {
            return null;
        }
        var token = call.Arguments.Where(argument => !argument.IsReceiver).Skip(1).ToList();
        if (
            token.Count > 1
            || token.Any(argument =>
                !IsCancellationToken(argument.Parameter.Type) || argument.Value is null
            )
        )
        {
            return null;
        }
        var callee = syntax.Expression.ToString();
        var fix = token is [{ Value: { } passed }]
            ? Fix.For(call).ReplaceWith($"{callee}({{0}}, {clock}, {{1}})", delay, passed)
            : Fix.For(call).ReplaceWith($"{callee}({{0}}, {clock})", delay);
        return call.Target.Name is "Delay" or "WaitAsync"
            ? fix.SafeWhen(change => KeepsCallWithClock(change))
            : null;
    }

    private static FixProposal? TimeWithClock(CodeObjectCreation creation, string clock)
    {
        if (
            creation.Syntax is not ObjectCreationExpressionSyntax { Type: var type }
            || creation.Constructor is not { Parameters: [var parameter] }
            || !IsType(parameter.Type, "System", "TimeSpan")
            || !(
                IsType(creation.Type, "System.Threading", "PeriodicTimer")
                || IsType(creation.Type, "System.Threading", "CancellationTokenSource")
            )
            || creation.Argument(parameter.Name) is not { } period
        )
        {
            return null;
        }
        return Fix.For(creation)
            .ReplaceWith($"new {type}({{0}}, {clock})", period)
            .SafeWhen(change => KeepsCallWithClock(change));
    }

    // The rewritten call or creation targets the same type and name, with the injected clock as its only TimeProvider.
    private static bool KeepsCallWithClock(ExpressionChange change)
    {
        var (before, after) = (change.Before.Operation, change.After.Operation);
        var (beforeMethod, afterMethod, arguments) = (before, after) switch
        {
            (IInvocationOperation b, IInvocationOperation a) => (
                b.TargetMethod,
                a.TargetMethod,
                a.Arguments
            ),
            (
                IObjectCreationOperation { Constructor: { } b },
                IObjectCreationOperation { Constructor: { } a } created
            ) => (b, a, created.Arguments),
            _ => (null, null, default),
        };
        return beforeMethod is not null
            && afterMethod is not null
            && beforeMethod.Name == afterMethod.Name
            && change.Same(beforeMethod.ContainingType, afterMethod.ContainingType)
            && afterMethod.Parameters.Length == beforeMethod.Parameters.Length + 1
            && arguments.Count(argument => IsTimeProvider(argument.Parameter?.Type)) == 1
            && arguments
                .Where(argument => IsTimeProvider(argument.Parameter?.Type))
                .All(argument => IsInjectedClock(argument.Value));
    }
}
