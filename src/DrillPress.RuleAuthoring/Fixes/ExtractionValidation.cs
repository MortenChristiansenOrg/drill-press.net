using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

internal static class ExtractionValidation
{
    internal static bool Validate(
        ExtractionPlan plan,
        RewriteContext context,
        Func<ExtractionEvidence, ProofResult> provesBehavior
    )
    {
        var destinations = context
            .Original.Sources.Where(source =>
                source.Document.FileIdentity == plan.Destination.Document.FileIdentity
            )
            .ToArray();
        if (destinations.Length != 1)
            return false;
        var destination = destinations[0];
        var beforePart =
            destination
                .Tree.GetRoot(context.Original.CancellationToken)
                .FindNode(plan.Part.Span, getInnermostNodeForTie: true) as TypeDeclarationSyntax;
        if (
            beforePart is null
            || beforePart.Span != plan.Part.Span
            || destination.Model.GetDeclaredSymbol(beforePart) is not INamedTypeSymbol beforeOwner
            || context.MapToken(destination, beforePart.CloseBraceToken)?.Parent
                is not TypeDeclarationSyntax afterPart
        )
            return false;
        var afterModel = context.Rewritten.GetSemanticModel(afterPart.SyntaxTree);
        if (
            afterModel.GetDeclaredSymbol(afterPart) is not INamedTypeSymbol afterOwner
            || !RewriteSymbols.Same(beforeOwner, afterOwner, context)
        )
            return false;
        var members = afterOwner.GetMembers(plan.Name);
        if (members.Length != 1)
            return false;
        var member = members[0];
        var occurrences = ReadOccurrences(plan, context);
        if (occurrences is null || occurrences.Count == 0 || !SameGroup(plan, occurrences))
            return false;
        if (!SameMember(plan, context, beforeOwner, member, occurrences[0]))
            return false;
        var evidence = new List<RewriteEvidence>();
        foreach (var occurrence in occurrences)
        {
            var mapped = context.Map(occurrence.Expression.Source, occurrence.Expression.Syntax);
            if (
                mapped is not { After: ExpressionSyntax after }
                || !SymbolEqualityComparer.Default.Equals(
                    mapped.Model.GetSymbolInfo(after).Symbol,
                    member
                )
            )
                return false;
            IReadOnlyList<InputRewrite> inputs = [];
            if (occurrence.Capture is { } capture)
            {
                if (
                    after is not InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } call
                )
                    return false;
                inputs = [new(capture.Syntax, [call.ArgumentList.Arguments[0].Expression])];
            }
            var change = new RewriteEvidence(context, mapped, inputs);
            if (
                RewriteChecks.SameEnclosingBindings(change) != ProofResult.Proven
                || RewriteChecks.SameRetainedBindings(change) != ProofResult.Proven
                || inputs.Count > 0
                    && RewriteChecks.SameEvaluationCounts(change) != ProofResult.Proven
            )
                return false;
            evidence.Add(change);
        }
        return RewriteChecks.SameCompilerSuppliedArguments(evidence[0]) == ProofResult.Proven
            && SameOtherBindings(context)
            && provesBehavior(
                new(context, beforeOwner, afterOwner, member, plan.Reused, evidence.AsReadOnly())
            ) == ProofResult.Proven;
    }

    private static List<ExpressionOccurrence>? ReadOccurrences(
        ExtractionPlan plan,
        RewriteContext context
    )
    {
        var result = new List<ExpressionOccurrence>();
        foreach (var selected in plan.Group.Occurrences)
        foreach (
            var source in context.Original.Sources.Where(source =>
                source.Document.FileIdentity == selected.Expression.Source.Document.FileIdentity
            )
        )
        {
            var syntax = source
                .Tree.GetRoot(context.Original.CancellationToken)
                .FindNode(selected.Expression.Syntax.Span, getInnermostNodeForTie: true);
            if (
                syntax is not ExpressionSyntax expression
                || syntax.Span != selected.Expression.Syntax.Span
                || !ExtractionSyntax.Eligible(source, syntax)
            )
                return null;
            var candidate = new CodeExpression(source, expression);
            var shape = ExpressionShape.Read(candidate, plan.Group.Options);
            if (shape is null)
                return null;
            result.Add(new(candidate, shape.Capture));
        }
        return result;
    }

    private static bool SameGroup(
        ExtractionPlan plan,
        IReadOnlyList<ExpressionOccurrence> occurrences
    )
    {
        var key = ExpressionShape.Read(occurrences[0].Expression, plan.Group.Options)?.Key;
        return key is not null
            && occurrences.All(occurrence =>
                ExpressionShape.Read(occurrence.Expression, plan.Group.Options)?.Key == key
            );
    }

    private static bool SameMember(
        ExtractionPlan plan,
        RewriteContext context,
        INamedTypeSymbol beforeOwner,
        ISymbol member,
        ExpressionOccurrence first
    )
    {
        if (plan.Group.Kind == ExpressionGroupKind.Constant)
        {
            if (
                member is not IFieldSymbol { IsConst: true, HasConstantValue: true } field
                || ExpressionShape.Read(first.Expression, null)?.Key
                    != ExpressionShape.ConstantKey(field.Type, field.ConstantValue)
            )
                return false;
            return plan.Reused
                ? beforeOwner.GetMembers(plan.Name).SingleOrDefault() is IFieldSymbol original
                    && RewriteSymbols.Same(original, field, context)
                : field.DeclaredAccessibility == Accessibility.Private;
        }
        if (
            member
                is not IMethodSymbol
                {
                    IsStatic: true,
                    DeclaredAccessibility: Accessibility.Private,
                    Parameters.Length: 1,
                    ReturnType.SpecialType: SpecialType.System_String
                } method
            || method.Parameters[0].RefKind != RefKind.None
            || RewriteSymbols.Identity(method.Parameters[0].Type)
                != RewriteSymbols.Identity(first.Capture!.Type)
            || method
                .DeclaringSyntaxReferences.SingleOrDefault()
                ?.GetSyntax(context.Original.CancellationToken)
                is not MethodDeclarationSyntax { ExpressionBody.Expression: { } body }
        )
            return false;
        var tree = body.SyntaxTree;
        var originalSource = context.Original.Sources.SingleOrDefault(source =>
            context.TreeFor(source) == tree
        );
        if (
            originalSource is null
            || context.RewrittenSource(originalSource) is not { } rewrittenSource
        )
            return false;
        var options = plan.Group.Options!;
        var rewrittenOptions = new OneHoleTemplateOptions(
            options.Shapes,
            expression =>
                expression.Operation is IParameterReferenceOperation reference
                && SymbolEqualityComparer.Default.Equals(reference.Parameter, method.Parameters[0]),
            options.AllowedCalls,
            options.MaximumNodes
        );
        return ExpressionShape.Read(new(rewrittenSource, body), rewrittenOptions)?.Key
            == ExpressionShape.Read(first.Expression, options)?.Key;
    }

    private static bool SameOtherBindings(RewriteContext context)
    {
        foreach (var source in context.Original.Sources)
        foreach (
            var before in source
                .Tree.GetRoot(context.Original.CancellationToken)
                .DescendantNodes()
                .OfType<ExpressionSyntax>()
        )
        {
            context.Original.CancellationToken.ThrowIfCancellationRequested();
            if (
                context.Edits.Any(edit =>
                    edit.FileIdentity == source.Document.FileIdentity
                    && edit.Length > 0
                    && new TextSpan(edit.Start, edit.Length).Contains(before.Span)
                )
            )
                continue;
            if (
                context.Map(source, before) is not { After: ExpressionSyntax after } mapped
                || !RewriteChecks.SameTypes(source.Model, before, mapped.Model, after)
                || !RewriteSymbols.Same(
                    source.Model.GetSymbolInfo(before).Symbol,
                    mapped.Model.GetSymbolInfo(after).Symbol,
                    context
                )
            )
                return false;
        }
        return true;
    }
}
