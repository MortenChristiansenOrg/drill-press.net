using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>Reads minimal API routes, their handlers and the calls configuring the routes and their groups.</summary>
public static class EndpointRoutes
{
    private static readonly CodeType _routeBuilderExtensions = CodeType.Named(
        "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions"
    );

    /// <summary>The methods that map one route to a handler.</summary>
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

    /// <summary>The framework method mapping a route group.</summary>
    private static readonly CodeMember _mapGroup = _routeBuilderExtensions.Member("MapGroup");

    /// <summary>Every route mapped in application code.</summary>
    public static CodeQuery<CodeEndpointRoute> All { get; } = Code.Calls.To(_mapRoute).Select(For);

    /// <summary>Every route group mapped in application code.</summary>
    public static CodeQuery<CodeEndpointRoute> Groups { get; } =
        Code.Calls.To(_mapGroup).Select(For);

    /// <summary>Whether a configured route chain contains a bound member.</summary>
    public static bool Calls(this CodeEndpointRoute route, CodeMember member) =>
        route.Chain.Any(call => member.Matches(call.TargetMethod));

    /// <summary>Whether a route or any visible group chain contains a bound member.</summary>
    public static bool CallsOnRouteOrGroup(this CodeEndpointRoute route, CodeMember member) =>
        route.Calls(member) || route.GroupChain.Any(call => member.Matches(call.TargetMethod));

    /// <summary>Reads a mapping, constant prefixes and handler; local initializers are followed up to ten hops per group.</summary>
    public static CodeEndpointRoute For(CodeInvocation mapping)
    {
        var operation = mapping.Operation;
        var groupChain = new List<IInvocationOperation>();
        var prefix = GroupPrefix(Receiver(operation), mapping.Source.Model, groupChain);
        var pattern = PatternOf(operation);
        var path = prefix is null || pattern is null ? null : prefix + pattern;
        var handler = HandlerOf(operation);
        return new(
            mapping,
            path,
            ChainedOnto(operation),
            groupChain,
            handler,
            HandlerMethod(handler)
        );
    }

    private static IReadOnlyList<IInvocationOperation> ChainedOnto(IInvocationOperation operation)
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
        for (var depth = 0; depth < 10; depth++)
        {
            builder = Unwrap(builder);
            if (builder is ILocalReferenceOperation local)
            {
                builder = Initializer(local.Local, model);
                continue;
            }
            // A chained call on a group, such as _mapGroup(...).RequireAuthorization(...), is still that group.
            while (
                builder is IInvocationOperation call
                && !_mapGroup.Matches(call.TargetMethod)
                && Receiver(call) is { } inner
            )
            {
                builder = inner;
            }
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

    private static IOperation? HandlerOf(IInvocationOperation operation) =>
        operation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name is "handler" or "requestDelegate"
        )
            is { } argument
            ? Unwrap(argument.Value) is IDelegateCreationOperation creation
                ? creation.Target
                : Unwrap(argument.Value)
            : null;

    private static IMethodSymbol? HandlerMethod(IOperation? handler) =>
        handler switch
        {
            IMethodReferenceOperation reference => reference.Method,
            IAnonymousFunctionOperation lambda => lambda.Symbol,
            _ => null,
        };

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } or IParenthesizedOperation)
        {
            operation = operation switch
            {
                IConversionOperation conversion => conversion.Operand,
                IParenthesizedOperation parenthesized => parenthesized.Operand,
                _ => operation,
            };
        }
        return operation;
    }
}
