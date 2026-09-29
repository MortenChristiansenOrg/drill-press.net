using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>Bounded structural proofs over the actual fully rewritten compilation. These do not establish arbitrary behavioral equivalence.</summary>
public static class RewriteChecks
{
    /// <summary>Preserves every original source expression's bound symbol, type and conversion in this compilation. Use for structural edits that intentionally change no expressions.</summary>
    public static ProofResult SameSourceBindings(RewriteEvidence change)
    {
        foreach (var source in change.Context.Original.Sources)
        foreach (
            var expression in source
                .Tree.GetRoot(source.Project.CancellationToken)
                .DescendantNodes()
                .OfType<ExpressionSyntax>()
        )
        {
            if (
                change.Context.Map(source, expression)
                is not { After: ExpressionSyntax after } mapped
            )
                return ProofResult.Unknown;
            if (
                !SameTypes(source.Model, expression, mapped.Model, after)
                || !RewriteSymbols.Same(
                    source.Model.GetSymbolInfo(expression).Symbol,
                    mapped.Model.GetSymbolInfo(after).Symbol,
                    change.Context
                )
            )
                return ProofResult.Disproven;
        }
        return ProofResult.Proven;
    }

    /// <summary>Preserves expression and converted types plus enclosing member/operator binding, excluding the intentionally replaced root's symbol.</summary>
    public static ProofResult SameEnclosingBindings(RewriteEvidence change)
    {
        foreach (var before in change.Before.AncestorsAndSelf().OfType<ExpressionSyntax>())
        {
            if (
                change.Context.Map(change.Source, before)
                is not { After: ExpressionSyntax after } mapped
            )
                return ProofResult.Unknown;
            if (!SameTypes(change.BeforeModel, before, mapped.Model, after))
                return ProofResult.Disproven;
            if (
                before != change.Before
                && !RewriteSymbols.Same(
                    change.BeforeModel.GetSymbolInfo(before).Symbol,
                    mapped.Model.GetSymbolInfo(after).Symbol,
                    change.Context
                )
            )
                return ProofResult.Disproven;
        }
        return ProofResult.Proven;
    }

    /// <summary>Checks that registered retained inputs keep their syntax, symbol and conversions, independently of evaluation count.</summary>
    public static ProofResult SameRetainedBindings(RewriteEvidence change)
    {
        foreach (var input in change.Inputs)
        foreach (var after in input.After)
            if (
                !SyntaxFactory.AreEquivalent(input.Before, after)
                || !SameTypes(change.BeforeModel, input.Before, change.AfterModel, after)
                || !RewriteSymbols.Same(
                    change.BeforeModel.GetSymbolInfo(input.Before).Symbol,
                    change.AfterModel.GetSymbolInfo(after).Symbol,
                    change.Context
                )
            )
                return ProofResult.Disproven;
        return ProofResult.Proven;
    }

    /// <summary>Requires each registered original occurrence to appear exactly once; missing input registration is unknown.</summary>
    public static ProofResult SameEvaluationCounts(RewriteEvidence change) =>
        change.Inputs.Count == 0 ? ProofResult.Unknown
        : change
            .Inputs.GroupBy(input => input.Before.Span)
            .All(group => group.Sum(input => input.After.Count) == 1)
            ? ProofResult.Proven
        : ProofResult.Disproven;

    /// <summary>Compares mapped inputs and remaining observable evaluations in straight-line expressions. Conditional/deferred shapes return unknown.</summary>
    public static ProofResult SameEvaluationSequence(RewriteEvidence change)
    {
        var counts = SameEvaluationCounts(change);
        if (counts != ProofResult.Proven)
            return counts;
        var before = new Dictionary<TextSpan, int>();
        var after = new Dictionary<TextSpan, int>();
        foreach (var (input, index) in change.Inputs.Select((input, index) => (input, index)))
        {
            if (!before.TryAdd(EvaluationTrace.OperandSpan(input.Before), index))
                return ProofResult.Unknown;
            foreach (var occurrence in input.After)
                if (!after.TryAdd(EvaluationTrace.OperandSpan(occurrence), index))
                    return ProofResult.Unknown;
        }
        var first = EvaluationTrace.Read(
            Operation(change.BeforeModel, change.Before),
            before,
            change.Context.Original.CancellationToken
        );
        var second = EvaluationTrace.Read(
            Operation(change.AfterModel, change.After),
            after,
            change.Context.Original.CancellationToken
        );
        if (
            first is null
            || second is null
            || first.Count(item => item.StartsWith("input:")) != before.Count
            || second.Count(item => item.StartsWith("input:")) != after.Count
        )
            return ProofResult.Unknown;
        return first.SequenceEqual(second) ? ProofResult.Proven : ProofResult.Disproven;
    }

    /// <summary>Rejects loss of an instance null check unless the receiver is intrinsically non-null. Compiler annotations alone do not prove runtime non-nullness.</summary>
    public static ProofResult SameReceiverNullBehavior(RewriteEvidence change)
    {
        if (
            Operation(change.BeforeModel, change.Before) is IConditionalAccessOperation
            || Operation(change.AfterModel, change.After) is IConditionalAccessOperation
        )
            return ProofResult.Unknown;
        var before = Operation(change.BeforeModel, change.Before);
        var after = Operation(change.AfterModel, change.After);
        var oldReceiver = Receiver(before);
        var newReceiver = Receiver(after);
        if (oldReceiver is null && newReceiver is null)
            return ProofResult.Proven;
        if (oldReceiver is not null && newReceiver is not null)
            return
                SyntaxFactory.AreEquivalent(oldReceiver.Syntax, newReceiver.Syntax)
                && RewriteSymbols.Same(Member(before), Member(after), change.Context)
                && SameEvaluationSequence(change) == ProofResult.Proven
                ? ProofResult.Proven
                : ProofResult.Unknown;
        var receiver = oldReceiver ?? newReceiver;
        return
            receiver
                is IInstanceReferenceOperation
                    or IObjectCreationOperation
                    or ILiteralOperation { ConstantValue: { HasValue: true, Value: not null } }
            ? ProofResult.Proven
            : ProofResult.Unknown;
    }

    private static IOperation? Receiver(IOperation? operation) =>
        operation switch
        {
            IInvocationOperation call => call.Instance,
            IMemberReferenceOperation reference => reference.Instance,
            _ => null,
        };

    private static ISymbol? Member(IOperation? operation) =>
        operation switch
        {
            IInvocationOperation call => call.TargetMethod,
            IMemberReferenceOperation reference => reference.Member,
            _ => null,
        };

    /// <summary>Compares compiler-supplied argument constants on retained calls, including caller-line and caller-argument-expression values outside the edited expression.</summary>
    public static ProofResult SameCompilerSuppliedArguments(RewriteEvidence change) =>
        CompilerSuppliedArguments(change, null);

    internal static ProofResult CompilerSuppliedArguments(
        RewriteEvidence change,
        SyntaxNode? approvedTransition
    )
    {
        foreach (
            var source in change.Context.Original.Sources.Where(source =>
                change.Context.Edits.Any(edit => edit.FileIdentity == source.Document.FileIdentity)
            )
        )
        {
            foreach (
                var syntax in source
                    .Tree.GetRoot(source.Project.CancellationToken)
                    .DescendantNodes()
                    .Where(node =>
                        node
                            is InvocationExpressionSyntax
                                or BaseObjectCreationExpressionSyntax
                                or ElementAccessExpressionSyntax
                    )
            )
            {
                if (syntax == approvedTransition)
                    continue;
                var oldArguments = Arguments(source.Model.GetOperation(syntax));
                if (
                    oldArguments is null
                    || oldArguments.All(argument =>
                        argument.ArgumentKind != ArgumentKind.DefaultValue
                    )
                )
                    continue;
                if (change.Context.Map(source, syntax) is not { } mapped)
                    return ProofResult.Unknown;
                var newArguments = Arguments(mapped.Model.GetOperation(mapped.After));
                if (newArguments is null)
                {
                    if (
                        syntax == change.Before
                        || change.Context.Edits.Any(edit =>
                            edit.FileIdentity == source.Document.FileIdentity
                            && edit.Start == syntax.SpanStart
                            && edit.Length == syntax.Span.Length
                        )
                    )
                        continue;
                    return ProofResult.Unknown;
                }
                var before = oldArguments
                    .Where(argument => argument.ArgumentKind == ArgumentKind.DefaultValue)
                    .Select(DefaultIdentity);
                var after = newArguments
                    .Where(argument => argument.ArgumentKind == ArgumentKind.DefaultValue)
                    .Select(DefaultIdentity);
                if (!before.SequenceEqual(after))
                    return ProofResult.Disproven;
            }
        }
        return ProofResult.Proven;
    }

    internal static IOperation? Operation(SemanticModel model, SyntaxNode node)
    {
        while (node is ParenthesizedExpressionSyntax parenthesized)
            node = parenthesized.Expression;
        return model.GetOperation(node);
    }

    private static IEnumerable<IArgumentOperation>? Arguments(IOperation? operation) =>
        operation switch
        {
            IInvocationOperation call => call.Arguments,
            IObjectCreationOperation creation => creation.Arguments,
            IPropertyReferenceOperation property => property.Arguments,
            _ => null,
        };

    private static (string?, int?, string?, bool, object?) DefaultIdentity(
        IArgumentOperation argument
    ) =>
        (
            argument.Parameter?.Name,
            argument.Parameter?.Ordinal,
            RewriteSymbols.Identity(argument.Value.Type),
            argument.Value.ConstantValue.HasValue,
            argument.Value.ConstantValue.HasValue ? argument.Value.ConstantValue.Value : null
        );

    internal static bool SameTypes(
        SemanticModel beforeModel,
        ExpressionSyntax before,
        SemanticModel afterModel,
        ExpressionSyntax after
    )
    {
        var first = beforeModel.GetTypeInfo(before);
        var second = afterModel.GetTypeInfo(after);
        var oldConversion = beforeModel.GetConversion(before);
        var newConversion = afterModel.GetConversion(after);
        return first.Type?.TypeKind != TypeKind.Error
            && second.Type?.TypeKind != TypeKind.Error
            && RewriteSymbols.Identity(first.Type) == RewriteSymbols.Identity(second.Type)
            && RewriteSymbols.Identity(first.ConvertedType)
                == RewriteSymbols.Identity(second.ConvertedType)
            && oldConversion.IsIdentity == newConversion.IsIdentity
            && oldConversion.IsImplicit == newConversion.IsImplicit
            && oldConversion.IsUserDefined == newConversion.IsUserDefined
            && oldConversion.IsNumeric == newConversion.IsNumeric
            && oldConversion.IsReference == newConversion.IsReference
            && oldConversion.IsEnumeration == newConversion.IsEnumeration
            && oldConversion.IsDynamic == newConversion.IsDynamic
            && oldConversion.IsConstantExpression == newConversion.IsConstantExpression
            && oldConversion.IsBoxing == newConversion.IsBoxing
            && oldConversion.IsUnboxing == newConversion.IsUnboxing
            && oldConversion.IsNullable == newConversion.IsNullable
            && RewriteSymbols.Identity(oldConversion.MethodSymbol)
                == RewriteSymbols.Identity(newConversion.MethodSymbol);
    }
}
