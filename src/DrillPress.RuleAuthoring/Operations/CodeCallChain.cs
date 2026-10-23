using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>A fluent call chain: calls that each receive the previous call's result, such as <c>Where</c>, <c>OrderBy</c> and <c>ToList</c> in <c>items.Where(a).OrderBy(b).ToList()</c>. Instance, extension (in both spellings) and conditional-access calls link; an <c>await</c> or any other expression between two calls ends the chain.</summary>
public sealed class CodeCallChain
{
    internal CodeCallChain(IReadOnlyList<CodeInvocation> calls) => Calls = calls;

    /// <summary>The calls in evaluation order: the call on the root first, the call whose result leaves the chain last.</summary>
    public IReadOnlyList<CodeInvocation> Calls { get; }

    /// <summary>The first call, which works on <see cref="Root"/>.</summary>
    public CodeInvocation First => Calls[0];

    /// <summary>The last call, whose result the rest of the code uses.</summary>
    public CodeInvocation Last => Calls[^1];

    /// <summary>The value the chain starts from, such as a local, parameter, field or creation; null when the first call is an ordinary static call.</summary>
    public CodeExpression? Root => First.Receiver;

    /// <summary>The local the chain starts from, or null when its root is anything else.</summary>
    public ILocalSymbol? Local =>
        CallChainLinks.Receiver(First.Operation) is ILocalReferenceOperation reference
            ? reference.Local
            : null;

    /// <summary>The calls evaluated before the call, which produce its receiver; empty when the call is not in this chain.</summary>
    public IReadOnlyList<CodeInvocation> Before(CodeInvocation call) =>
        IndexOf(call) is var index and >= 0 ? Calls.Take(index).ToArray() : [];

    /// <summary>The calls that continue from the call's result; empty when the call is not in this chain.</summary>
    public IReadOnlyList<CodeInvocation> After(CodeInvocation call) =>
        IndexOf(call) is var index and >= 0 ? Calls.Skip(index + 1).ToArray() : [];

    /// <summary>
    /// This chain and every chain connected to it through the local it starts from, in source order: the chains whose
    /// results are assigned to the local (its initializer and assignments, as in <c>q = q.Where(...)</c>) and the chains
    /// that start from it (as in builders that change in place, <c>query.AndAlso().WhereGreaterThan(...)</c>). Only this
    /// chain when it does not start from a local.
    /// </summary>
    /// <remarks>Reads the whole executable body around the chain, including lambdas and local functions, without regard to
    /// statement order or branches. It does not continue through a second local, such as <c>var q2 = q1.Where(...)</c>.</remarks>
    public IReadOnlyList<CodeCallChain> ThroughLocal()
    {
        if (Local is not { } local)
            return [this];
        IOperation body = First.Operation;
        while (body.Parent is not null)
            body = body.Parent;
        var chains = new List<CodeCallChain> { this };
        foreach (var operation in body.DescendantsAndSelf())
        {
            var connected = operation switch
            {
                IVariableDeclaratorOperation declarator
                    when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) =>
                    declarator.GetVariableInitializer()?.Value,
                ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment
                    when SymbolEqualityComparer.Default.Equals(target.Local, local) =>
                    assignment.Value,
                IInvocationOperation call
                    when CallChainLinks.Receiver(call) is ILocalReferenceOperation reference
                        && SymbolEqualityComparer.Default.Equals(reference.Local, local) => call,
                _ => null,
            };
            if (
                CallChainLinks.Unconverted(connected) is IInvocationOperation invocation
                && !chains.Any(chain => chain.Calls.Any(call => call.Operation == invocation))
            )
                chains.Add(Of(new CodeInvocation(First.Source, invocation)));
        }
        return chains.OrderBy(chain => chain.Last.Operation.Syntax.SpanStart).ToArray();
    }

    internal static CodeCallChain Of(CodeInvocation call)
    {
        var calls = new List<CodeInvocation>();
        for (
            var receiver = CallChainLinks.Receiver(call.Operation);
            receiver is IInvocationOperation inner;
            receiver = CallChainLinks.Receiver(inner)
        )
            calls.Add(new(call.Source, inner));
        calls.Reverse();
        calls.Add(call);
        for (
            var consumer = CallChainLinks.Consumer(call.Operation);
            consumer is not null;
            consumer = CallChainLinks.Consumer(consumer)
        )
            calls.Add(new(call.Source, consumer));
        return new(calls.AsReadOnly());
    }

    private int IndexOf(CodeInvocation call)
    {
        for (var index = 0; index < Calls.Count; index++)
            if (Calls[index].Equals(call))
                return index;
        return -1;
    }
}
