using DrillPress.Operations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Fixes;

internal static class ArgumentTransitionChecks
{
    internal static bool SameTypeArguments(
        IMethodSymbol before,
        IMethodSymbol after,
        RewriteContext context
    ) =>
        before.TypeArguments.Length == after.TypeArguments.Length
        && before
            .TypeArguments.Zip(after.TypeArguments)
            .All(pair => RewriteSymbols.Same(pair.First, pair.Second, context));

    internal static bool RetainedArguments(
        ArgumentRemovalEvidence change,
        IReadOnlyDictionary<string, string> parameters
    )
    {
        var source = change.Rewrite.Source;
        var before = new CodeInvocation(source, change.Before);
        // Only operation-backed fields are read from this view; rewritten semantic queries use AfterModel below.
        var after = new CodeInvocation(source, change.After);
        var original = before.Declaration;
        var rewritten = after.Declaration;
        if (
            original.IsStatic != rewritten.IsStatic
            || original.IsExtensionMethod != rewritten.IsExtensionMethod
            || before.IsConditional != after.IsConditional
            || parameters.Count != original.Parameters.Length - 1
            || original
                .Parameters.Where(parameter => parameter.Name != change.Removed.Parameter.Name)
                .Any(parameter =>
                    !parameters.TryGetValue(parameter.Name, out var mapped)
                    || !rewritten.Parameters.Any(candidate =>
                        candidate.Name == mapped
                        && candidate.RefKind == parameter.RefKind
                        && RewriteSymbols.Identity(candidate.Type)
                            == RewriteSymbols.Identity(parameter.Type)
                    )
                )
            || before
                .Arguments.Concat(after.Arguments)
                .Any(argument =>
                    argument.Kind is ArgumentKind.ParamArray or ArgumentKind.ParamCollection
                )
        )
            return false;
        var oldValues = before
            .Arguments.Where(argument =>
                argument.IsExplicit && argument.SourceIndex != change.Removed.SourceIndex
            )
            .ToArray();
        var newValues = after.Arguments.Where(argument => argument.IsExplicit).ToArray();
        if (oldValues.Length != newValues.Length)
            return false;
        for (var index = 0; index < oldValues.Length; index++)
        {
            var first = oldValues[index];
            var second = newValues[index];
            if (
                !parameters.TryGetValue(first.Parameter.Name, out var name)
                || name != second.Parameter.Name
                || first.RefKind != second.RefKind
                || first.Kind != second.Kind
                || first.Value?.Syntax is not { } oldSyntax
                || second.Value?.Syntax is not { } newSyntax
                || !SameInput(change, oldSyntax, newSyntax)
            )
                return false;
        }
        var oldReceiver = before.Receiver;
        var newReceiver = after.Receiver;
        if (oldReceiver is null || newReceiver is null)
            return oldReceiver is null && newReceiver is null;
        if (oldReceiver.IsImplicit || newReceiver.IsImplicit)
            return oldReceiver.IsImplicit == newReceiver.IsImplicit
                && RewriteSymbols.Identity(change.Before.Instance?.Type)
                    == RewriteSymbols.Identity(change.After.Instance?.Type);
        return SameInput(change, oldReceiver.Syntax, newReceiver.Syntax);
    }

    internal static bool SameDefaults(
        ArgumentRemovalEvidence change,
        IReadOnlyDictionary<string, string> parameters
    )
    {
        var before = change
            .Before.Arguments.Where(argument => argument.ArgumentKind == ArgumentKind.DefaultValue)
            .ToArray();
        var after = change
            .After.Arguments.Where(argument => argument.ArgumentKind == ArgumentKind.DefaultValue)
            .ToArray();
        return before.Length == after.Length
            && before.All(argument =>
                argument.Parameter is { } parameter
                && parameters.TryGetValue(parameter.Name, out var mapped)
                && after.SingleOrDefault(candidate => candidate.Parameter?.Name == mapped)
                    is { } counterpart
                && RewriteSymbols.Identity(argument.Value.Type)
                    == RewriteSymbols.Identity(counterpart.Value.Type)
                && argument.Value.ConstantValue.HasValue
                && counterpart.Value.ConstantValue.HasValue
                && Equals(argument.Value.ConstantValue.Value, counterpart.Value.ConstantValue.Value)
            );
    }

    private static bool SameInput(
        ArgumentRemovalEvidence change,
        ExpressionSyntax before,
        ExpressionSyntax after
    ) =>
        SyntaxFactory.AreEquivalent(before, after)
        && RewriteChecks.SameTypes(
            change.Rewrite.BeforeModel,
            before,
            change.Rewrite.AfterModel,
            after
        )
        && before
            .DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Zip(after.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
            .All(pair =>
                RewriteSymbols.Same(
                    change.Rewrite.BeforeModel.GetSymbolInfo(pair.First).Symbol,
                    change.Rewrite.AfterModel.GetSymbolInfo(pair.Second).Symbol,
                    change.Rewrite.Context
                )
                && RewriteChecks.SameTypes(
                    change.Rewrite.BeforeModel,
                    pair.First,
                    change.Rewrite.AfterModel,
                    pair.Second
                )
            );
}
