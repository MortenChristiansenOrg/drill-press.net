using DrillPress.Manifest;
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
    /// <remarks>When every edit lies inside a member body that remains that member's body, only those bodies are rebound: declarations are unchanged, so every other expression binds as before. Other edits rebind the whole compilation. Expressions edited by other proposals validated in the same batch are left to those proposals' proofs.</remarks>
    public static ProofResult SameSourceBindings(RewriteEvidence change) =>
        SourceBindings(change).Result;

    internal static CheckOutcome SourceBindings(RewriteEvidence change)
    {
        var context = change.Context;
        var own = change.OwnEdits ?? context.Edits;
        var foreign = context
            .Edits.Where(edit => change.OwnEdits is not null && !own.Contains(edit))
            .ToLookup(edit => edit.FileIdentity);
        var expressions = EditRegions.Read(context, own) is { } regions
            ? regions.SelectMany(region =>
                region
                    .Region.DescendantNodes()
                    .OfType<ExpressionSyntax>()
                    .Select(expression => (region.Source, Expression: expression))
            )
            : context.Original.Sources.SelectMany(source =>
                source
                    .Tree.GetRoot(source.Project.CancellationToken)
                    .DescendantNodes()
                    .OfType<ExpressionSyntax>()
                    .Select(expression => (Source: source, Expression: expression))
            );
        foreach (var (source, expression) in expressions)
        {
            context.Original.CancellationToken.ThrowIfCancellationRequested();
            if (EditsOverlap(foreign[source.Document.FileIdentity], expression.Span))
                continue;
            if (context.Map(source, expression) is not { After: ExpressionSyntax after } mapped)
                return CheckOutcome.Unknown(
                    FixReasons.Describe(source, expression)
                        + " has no corresponding expression after the edit"
                );
            if (!SameTypes(source.Model, expression, mapped.Model, after))
                return CheckOutcome.Disproven(
                    FixReasons.Describe(source, expression) + " changes type or conversion"
                );
            if (
                !RewriteSymbols.Same(
                    source.Model.GetSymbolInfo(expression).Symbol,
                    mapped.Model.GetSymbolInfo(after).Symbol,
                    context
                )
            )
                return CheckOutcome.Disproven(
                    FixReasons.Describe(source, expression) + " binds to a different symbol"
                );
        }
        return CheckOutcome.Proven;
    }

    private static bool EditsOverlap(IEnumerable<SourceEdit> edits, TextSpan span) =>
        edits.Any(edit =>
            edit.Length == 0
                ? span.Start < edit.Start && edit.Start < span.End
                : edit.Start < span.End && span.Start < edit.Start + edit.Length
        );

    /// <summary>Preserves expression and converted types plus enclosing member/operator binding, excluding the intentionally replaced root's symbol.</summary>
    /// <remarks>
    /// Already enforced by expression replacement through <see cref="ExpressionReplacement.SafeWhen"/>; add only transformation-specific obligations there.
    /// The replaced root keeps its converted type. A target-typed root, such as <c>default</c>, <c>[]</c>, <c>new()</c> or <c>null</c>, may replace or be replaced by an expression whose own type is exactly that converted type. A root whose value an expression statement discards may change its type. An enclosing tuple literal may change only inferred element names, and only when it converts to the same named tuple type.
    /// </remarks>
    public static ProofResult SameEnclosingBindings(RewriteEvidence change) =>
        EnclosingBindings(change).Result;

    internal static CheckOutcome EnclosingBindings(RewriteEvidence change)
    {
        foreach (var before in change.Before.AncestorsAndSelf().OfType<ExpressionSyntax>())
        {
            if (
                change.Context.Map(change.Source, before)
                is not { After: ExpressionSyntax after } mapped
            )
                return CheckOutcome.Unknown(
                    FixReasons.Describe(change.Source, before)
                        + " has no corresponding expression after the edit"
                );
            var root = before == change.Before;
            if (
                root
                    ? !SameRootTypes(change.BeforeModel, before, mapped.Model, after)
                    : !SameTypes(change.BeforeModel, before, mapped.Model, after)
                        && !SameTupleIgnoringInferredNames(
                            change.BeforeModel,
                            before,
                            mapped.Model,
                            after
                        )
            )
                return CheckOutcome.Disproven(
                    FixReasons.Describe(change.Source, before)
                        + (
                            root
                                ? " changes its converted type or conversion"
                                : " changes type or conversion"
                        )
                );
            if (
                !root
                && !RewriteSymbols.Same(
                    change.BeforeModel.GetSymbolInfo(before).Symbol,
                    mapped.Model.GetSymbolInfo(after).Symbol,
                    change.Context
                )
            )
                return CheckOutcome.Disproven(
                    FixReasons.Describe(change.Source, before) + " binds to a different symbol"
                );
        }
        return CheckOutcome.Proven;
    }

    // The value reaching the context keeps its type; target typing and discarded values cannot change that.
    private static bool SameRootTypes(
        SemanticModel beforeModel,
        ExpressionSyntax before,
        SemanticModel afterModel,
        ExpressionSyntax after
    )
    {
        if (
            Unparenthesized(before).Parent is ExpressionStatementSyntax
            && Unparenthesized(after).Parent is ExpressionStatementSyntax
        )
            return beforeModel.GetTypeInfo(before).Type?.TypeKind != TypeKind.Error
                && afterModel.GetTypeInfo(after).Type?.TypeKind != TypeKind.Error;
        if (SameTypes(beforeModel, before, afterModel, after))
            return true;
        var first = beforeModel.GetTypeInfo(before);
        var second = afterModel.GetTypeInfo(after);
        return first.ConvertedType is { TypeKind: not TypeKind.Error } converted
            && second.ConvertedType?.TypeKind is not (null or TypeKind.Error)
            && RewriteSymbols.Identity(converted) == RewriteSymbols.Identity(second.ConvertedType)
            && TargetTypedOrExact(first, beforeModel.GetConversion(before))
            && TargetTypedOrExact(second, afterModel.GetConversion(after));
    }

    private static SyntaxNode Unparenthesized(ExpressionSyntax expression)
    {
        SyntaxNode node = expression;
        while (node.Parent is ParenthesizedExpressionSyntax parent)
            node = parent;
        return node;
    }

    // A target-typed expression takes its type from the destination; any other expression must already have it.
    private static bool TargetTypedOrExact(TypeInfo info, Conversion conversion) =>
        conversion.MethodSymbol is null
        && (
            conversion.IsDefaultLiteral
            || conversion.IsCollectionExpression
            || conversion.IsObjectCreation
            || conversion.IsConditionalExpression
            || conversion.IsSwitchExpression
            || info.Type is null && conversion.IsImplicit
            || conversion.IsIdentity
                && RewriteSymbols.Identity(info.Type) == RewriteSymbols.Identity(info.ConvertedType)
        );

    // Inferred tuple element names, such as One in (One, "a"), are only observable through the tuple's own
    // type; converting to the same named tuple type discards them.
    private static bool SameTupleIgnoringInferredNames(
        SemanticModel beforeModel,
        ExpressionSyntax before,
        SemanticModel afterModel,
        ExpressionSyntax after
    )
    {
        if (before is not TupleExpressionSyntax || after is not TupleExpressionSyntax)
            return false;
        var first = beforeModel.GetTypeInfo(before);
        var second = afterModel.GetTypeInfo(after);
        return first.Type is { TypeKind: not TypeKind.Error }
            && second.Type is { TypeKind: not TypeKind.Error }
            && RewriteSymbols.Identity(first.ConvertedType)
                == RewriteSymbols.Identity(second.ConvertedType)
            && RewriteSymbols.WithoutTupleNames(first.Type)
                == RewriteSymbols.WithoutTupleNames(second.Type)
            && SameTupleConversion(
                beforeModel.GetConversion(before),
                afterModel.GetConversion(after)
            );
    }

    private static bool SameTupleConversion(Conversion before, Conversion after) =>
        before.MethodSymbol is null
        && after.MethodSymbol is null
        && before.IsIdentity == after.IsIdentity
        && before.IsTupleLiteralConversion == after.IsTupleLiteralConversion
        && before.IsImplicit == after.IsImplicit;

    /// <summary>Checks that registered retained inputs keep their syntax, symbol and conversions, independently of evaluation count.</summary>
    /// <remarks>Already enforced by expression replacement through <see cref="ExpressionReplacement.SafeWhen"/>; add only transformation-specific obligations there.</remarks>
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

    /// <summary>Requires each registered original occurrence to appear exactly once. With no registered inputs, proves only expressions whose bounded evaluation traces contain no child evaluations; other unregistered inputs remain unknown.</summary>
    public static ProofResult SameEvaluationCounts(RewriteEvidence change) =>
        change.Inputs.Count == 0 ? EmptyInputs(change)
        : change
            .Inputs.GroupBy(input => input.Before.Span)
            .All(group => group.Sum(input => input.After.Count) == 1)
            ? ProofResult.Proven
        : ProofResult.Disproven;

    private static ProofResult EmptyInputs(RewriteEvidence change) =>
        EvaluationTrace.Read(
            Operation(change.BeforeModel, change.Before),
            new Dictionary<TextSpan, int>(),
            change.Context.Original.CancellationToken
        )
            is { Count: 0 }
        && EvaluationTrace.Read(
            Operation(change.AfterModel, change.After),
            new Dictionary<TextSpan, int>(),
            change.Context.Original.CancellationToken
        )
            is { Count: 0 }
            ? ProofResult.Proven
            : ProofResult.Unknown;

    /// <summary>Compares mapped inputs and remaining observable evaluations in straight-line expressions, excluding constant reads and the replaced root through built-in Boolean negation. Conditional/deferred shapes return unknown.</summary>
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
        var before = EvaluationTrace.UnwrapBooleanNegation(
            Operation(change.BeforeModel, change.Before)
        );
        var after = EvaluationTrace.UnwrapBooleanNegation(
            Operation(change.AfterModel, change.After)
        );
        if (before is IConditionalAccessOperation || after is IConditionalAccessOperation)
            return ProofResult.Unknown;
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
    /// <remarks>Already enforced by expression replacement through <see cref="ExpressionReplacement.SafeWhen"/>; add only transformation-specific obligations there.</remarks>
    public static ProofResult SameCompilerSuppliedArguments(RewriteEvidence change) =>
        CompilerSuppliedArguments(change, null).Result;

    // Caller line numbers change only after an edit and caller argument expressions only around one, so calls
    // ending before a proposal's first edit in a file keep their values; other proposals check their own edits.
    internal static CheckOutcome CompilerSuppliedArguments(
        RewriteEvidence change,
        SyntaxNode? approvedTransition
    )
    {
        var own = change.OwnEdits ?? change.Context.Edits;
        foreach (var source in change.Context.Original.Sources)
        {
            var first = own.Where(edit => edit.FileIdentity == source.Document.FileIdentity)
                .Select(edit => (int?)edit.Start)
                .Min();
            if (first is null)
                continue;
            foreach (
                var syntax in source
                    .Tree.GetRoot(source.Project.CancellationToken)
                    .DescendantNodes(node => node.Span.End > first)
                    .Where(node =>
                        node.Span.End > first
                        && node
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
                    return CheckOutcome.Unknown(
                        FixReasons.Describe(source, syntax)
                            + " receives compiler-supplied arguments but has no corresponding call after the edit"
                    );
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
                    return CheckOutcome.Unknown(
                        FixReasons.Describe(source, syntax)
                            + " receives compiler-supplied arguments but is no longer a call after the edit"
                    );
                }
                // A sibling addition may intentionally supply an omitted parameter. Still compare
                // every remaining default, so caller-line changes are never hidden by batching.
                var suppliedElsewhere = IsOtherArgumentAddition(change, source, syntax, own)
                    ? newArguments
                        .Where(argument => argument.ArgumentKind == ArgumentKind.Explicit)
                        .Select(argument => argument.Parameter?.Name)
                        .ToHashSet()
                    : [];
                var before = oldArguments
                    .Where(argument =>
                        argument.ArgumentKind == ArgumentKind.DefaultValue
                        && !suppliedElsewhere.Contains(argument.Parameter?.Name)
                    )
                    .Select(DefaultIdentity);
                var after = newArguments
                    .Where(argument => argument.ArgumentKind == ArgumentKind.DefaultValue)
                    .Select(DefaultIdentity);
                if (!before.SequenceEqual(after))
                    return CheckOutcome.Disproven(
                        FixReasons.Describe(source, syntax)
                            + " receives different compiler-supplied arguments, such as a caller line number"
                    );
            }
        }
        return CheckOutcome.Proven;
    }

    // Only an exact insertion at another call's closing parenthesis can supply a previously
    // omitted parameter. Changes to its remaining defaults are still checked above.
    private static bool IsOtherArgumentAddition(
        RewriteEvidence change,
        AnalysisSource source,
        SyntaxNode syntax,
        IReadOnlyList<SourceEdit> own
    ) =>
        syntax is InvocationExpressionSyntax invocation
        && change.Context.Edits.Any(edit =>
            edit.FileIdentity == source.Document.FileIdentity
            && edit.Start == invocation.ArgumentList.CloseParenToken.SpanStart
            && edit.Length == 0
            && !own.Contains(edit)
        );

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
