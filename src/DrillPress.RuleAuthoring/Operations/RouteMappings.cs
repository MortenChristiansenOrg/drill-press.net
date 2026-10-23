using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

// Reads minimal API mappings: their constant patterns through route groups, their handlers and their configuration.
internal static class RouteMappings
{
    private static readonly CodeType _routeBuilderExtensions = CodeType.Named(
        "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions"
    );

    private static readonly ApiSet _mapRoute = new(
        _routeBuilderExtensions.Member("MapGet"),
        _routeBuilderExtensions.Member("MapPost"),
        _routeBuilderExtensions.Member("MapPut"),
        _routeBuilderExtensions.Member("MapDelete"),
        _routeBuilderExtensions.Member("MapPatch"),
        _routeBuilderExtensions.Member("MapMethods"),
        _routeBuilderExtensions.Member("MapFallback"),
        _routeBuilderExtensions.Member("Map")
    );

    private static readonly CodeMember _mapGroup = _routeBuilderExtensions.Member("MapGroup");

    // Group builders are followed through this many local initializers.
    private const int MaxLocalHops = 10;

    internal static CodeQuery<CodeRoute> Routes { get; } = Code.Calls.To(_mapRoute).Select(Read);

    internal static CodeQuery<CodeRoute> Groups { get; } = Code.Calls.To(_mapGroup).Select(Read);

    private static CodeRoute Read(CodeInvocation mapping)
    {
        var operation = mapping.Operation;
        var groupChain = new List<IInvocationOperation>();
        var prefix = GroupPrefix(Receiver(operation), mapping.Source.Model, groupChain);
        var pattern = PatternOf(operation);
        var handler = HandlerOf(operation);
        return new(
            mapping,
            prefix is null || pattern is null ? null : prefix + pattern,
            Wrap(mapping.Source, ChainedOnto(operation)),
            Wrap(mapping.Source, groupChain),
            handler?.Syntax is ExpressionSyntax syntax ? new(mapping.Source, syntax) : null,
            handler switch
            {
                IMethodReferenceOperation reference => reference.Method,
                IAnonymousFunctionOperation lambda => lambda.Symbol,
                _ => null,
            }
        );
    }

    private static IReadOnlyList<CodeInvocation> Wrap(
        AnalysisSource source,
        IEnumerable<IInvocationOperation> calls
    ) => calls.Select(call => new CodeInvocation(source, call)).ToArray();

    private static List<IInvocationOperation> ChainedOnto(IInvocationOperation operation)
    {
        var calls = new List<IInvocationOperation>();
        for (
            var next = CallChainLinks.Consumer(operation);
            next is not null;
            next = CallChainLinks.Consumer(next)
        )
            calls.Add(next);
        return calls;
    }

    private static IOperation? Receiver(IInvocationOperation operation) =>
        operation.TargetMethod.IsExtensionMethod && operation.Arguments.Length > 0
            ? Unwrap(operation.Arguments[0].Value)
        : operation.Instance is { } instance ? Unwrap(instance)
        : null;

    // The path prefix of the groups a builder belongs to: "" for the application itself, null when it can't be read.
    private static string? GroupPrefix(
        IOperation? builder,
        SemanticModel model,
        List<IInvocationOperation> groupChain
    )
    {
        for (var depth = 0; depth < MaxLocalHops; depth++)
        {
            builder = Unwrap(builder);
            if (builder is ILocalReferenceOperation local)
            {
                builder = Initializer(local.Local, model);
                continue;
            }
            // A chained call on a group, such as MapGroup(...).RequireAuthorization(...), is still that group.
            while (
                builder is IInvocationOperation call
                && !_mapGroup.Matches(call.TargetMethod)
                && Receiver(call) is { } inner
            )
                builder = inner;
            if (builder is IInvocationOperation group && _mapGroup.Matches(group.TargetMethod))
            {
                groupChain.AddRange(ChainedOnto(group));
                var outer = GroupPrefix(Receiver(group), model, groupChain);
                return outer is null || PatternOf(group) is not { } prefix ? null : outer + prefix;
            }
            return builder is IParameterReferenceOperation or IInstanceReferenceOperation
                ? ""
                : null;
        }
        return null;
    }

    private static IOperation? Initializer(ILocalSymbol local, SemanticModel model) =>
        local.DeclaringSyntaxReferences is [var reference]
        && reference.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: var value }
        && value.SyntaxTree == model.SyntaxTree
            ? model.GetOperation(value)
            : null;

    private static string? PatternOf(IInvocationOperation operation) =>
        operation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name is "pattern" or "prefix"
        )
            is { } argument
            ? Unwrap(argument.Value)?.ConstantValue is { HasValue: true, Value: string text }
                ? text
                : null
            : "";

    // The unwrapped method reference or lambda passed as the handler.
    private static IOperation? HandlerOf(IInvocationOperation operation) =>
        operation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name is "handler" or "requestDelegate"
        )
            is { } argument
            ? Unwrap(argument.Value) is IDelegateCreationOperation creation
                ? creation.Target
                : Unwrap(argument.Value)
            : null;

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } or IParenthesizedOperation)
            operation = operation switch
            {
                IConversionOperation conversion => conversion.Operand,
                IParenthesizedOperation parenthesized => parenthesized.Operand,
                _ => operation,
            };
        return operation;
    }
}
