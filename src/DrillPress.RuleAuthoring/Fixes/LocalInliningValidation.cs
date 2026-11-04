using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

// Proves an inlining in one rewritten compilation: every membership of the edited file plans the same edits,
// and the moved initializer binds exactly as it did in the declaration.
internal static class LocalInliningValidation
{
    internal static string? Validate(
        RewriteContext context,
        LocalInliningPlan selected,
        TextSpan declaration,
        bool ordered,
        Func<InlineChange, bool> proof,
        IReadOnlyList<SourceEdit> edits
    )
    {
        var memberships = context
            .Original.Sources.Where(source =>
                source.Document.FileIdentity == selected.Source.Document.FileIdentity
            )
            .ToArray();
        if (memberships.Length == 0)
            return "the edited file is not part of this compilation";
        foreach (var source in memberships)
        {
            var node = source
                .Tree.GetRoot(context.Original.CancellationToken)
                .FindNode(declaration, getInnermostNodeForTie: true);
            // A linked membership can bind, and so gate, differently, such as under other preprocessor symbols.
            var current = selected;
            if (source != selected.Source)
            {
                current = LocalInliningPlan.Create(
                    source,
                    node.Span == declaration ? node : null,
                    ordered,
                    out var reason
                );
                if (current is null)
                    return reason;
            }
            if (!current.Edits.SequenceEqual(edits))
                return "the declaration is inlined differently in this compilation";
            if (Binds(context, current, edits) is { } changed)
                return changed;
            if (!proof(new InlineChange(current, context)))
                return "the rule's SafeWhen proof returned false";
        }
        return null;
    }

    private static string? Binds(
        RewriteContext context,
        LocalInliningPlan plan,
        IReadOnlyList<SourceEdit> edits
    )
    {
        var source = plan.Source;
        if (context.Map(source, plan.Read) is not { After: ExpressionSyntax after } mapped)
            return "the read has no counterpart after the edit";
        var inlined =
            after is ParenthesizedExpressionSyntax parenthesized
            && !SyntaxFactory.AreEquivalent(after, plan.Initializer)
                ? parenthesized.Expression
                : after;
        if (!SyntaxFactory.AreEquivalent(inlined, plan.Initializer))
            return "the inlined initializer parses differently";
        var evidence = new RewriteEvidence(
            context,
            mapped,
            [new InputRewrite(plan.Initializer, [inlined])],
            edits
        );
        if (RewriteChecks.EnclosingBindings(evidence) is { Holds: false } enclosing)
            return enclosing.Reason;
        if (SameInitializer(context, plan, mapped.Model, inlined) is { } changed)
            return changed;
        var token = context.Original.CancellationToken;
        if (
            source.Model.GetTypeInfo(plan.Read, token).Nullability.FlowState
            != mapped.Model.GetTypeInfo(inlined, token).Nullability.FlowState
        )
            return "the read's nullable state would change";
        return RewriteChecks.CompilerSuppliedArguments(evidence, null) is { Holds: false } supplied
            ? supplied.Reason
            : null;
    }

    // Each expression of the moved initializer keeps its symbol, type and conversion; its root keeps its
    // natural type and symbol, converting now to the read's destination.
    private static string? SameInitializer(
        RewriteContext context,
        LocalInliningPlan plan,
        SemanticModel model,
        ExpressionSyntax inlined
    )
    {
        var source = plan.Source;
        var before = plan.Initializer.DescendantNodesAndSelf().OfType<ExpressionSyntax>().ToArray();
        var after = inlined.DescendantNodesAndSelf().OfType<ExpressionSyntax>().ToArray();
        if (before.Length != after.Length)
            return "the inlined initializer parses differently";
        for (var index = 0; index < before.Length; index++)
        {
            var root = index == 0;
            var same = root
                ? RewriteSymbols.Identity(source.Model.GetTypeInfo(before[0]).Type)
                    == RewriteSymbols.Identity(model.GetTypeInfo(after[0]).Type)
                : RewriteChecks.SameTypes(source.Model, before[index], model, after[index]);
            if (!same)
                return FixReasons.Describe(source, before[index])
                    + " changes type or conversion after inlining";
            if (
                !SameSymbol(
                    context,
                    plan,
                    inlined,
                    source.Model.GetSymbolInfo(before[index]).Symbol,
                    model.GetSymbolInfo(after[index]).Symbol
                )
            )
                return FixReasons.Describe(source, before[index])
                    + " binds to a different symbol after inlining";
        }
        return null;
    }

    // A symbol declared within the initializer, such as a lambda or its parameter, moves with it.
    private static bool SameSymbol(
        RewriteContext context,
        LocalInliningPlan plan,
        ExpressionSyntax inlined,
        ISymbol? before,
        ISymbol? after
    )
    {
        if (
            before?.DeclaringSyntaxReferences is not [var declared]
            || declared.SyntaxTree != plan.Source.Tree
            || !plan.Initializer.Span.Contains(declared.Span)
        )
            return RewriteSymbols.Same(before, after, context);
        return after is not null
            && after.Kind == before.Kind
            && after.Name == before.Name
            && after.DeclaringSyntaxReferences is [var moved]
            && moved.SyntaxTree == inlined.SyntaxTree
            && moved.Span.Start - inlined.SpanStart
                == declared.Span.Start - plan.Initializer.SpanStart
            && moved.Span.Length == declared.Span.Length;
    }
}
