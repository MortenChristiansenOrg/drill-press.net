using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.SampleRules.Fixes;

/// <summary>Removes the comparer only from Enumerable.Distinct&lt;string&gt;'s documented equivalent overload pair.</summary>
internal static class OrdinalComparerFix
{
    /// <summary>Diagnoses Ordinal references inside arguments, including calls outside the fix allowlist.</summary>
    public static RuleCondition<MemberReference> IsArgument { get; } =
        new(reference =>
            reference
                .Syntax?.Ancestors()
                .TakeWhile(node =>
                    node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
                )
                .OfType<ArgumentSyntax>()
                .Any() == true
        );

    /// <summary>Declares the exact framework pair and separately certifies its default value and removable evaluation.</summary>
    public static FixProposal? Create(MemberReference reference)
    {
        if (reference.AsArgument() is not { Parameter.Name: "comparer" } argument)
            return null;
        var distinct = CodeType.Framework("System.Linq.Enumerable").Member("Distinct");
        var enumerable = CodeType.Framework("System.Collections.Generic.IEnumerable<>");
        var comparer = CodeType.Framework("System.Collections.Generic.IEqualityComparer<>");
        var ordinal = CodeType.Of<StringComparer>().Member(nameof(StringComparer.Ordinal));
        return Fix.For(argument)
            .Remove()
            .ExpectOverloadChange(
                distinct.WithParameters(enumerable, comparer),
                distinct.WithParameters(enumerable)
            )
            .RequireRemovedValue(change => change.RemovedValue?.RefersTo(ordinal) == true)
            // StringComparer.Ordinal is the framework's immutable singleton. For this exact string
            // Distinct pair, omitting its getter evaluation has no observable contract effect.
            .RequireRemovedEvaluation(change =>
                change.RemovedValue?.RefersTo(ordinal) == true
                && change.Expected.Before.TypeArguments
                    is [{ SpecialType: SpecialType.System_String }]
            )
            .SafeWhen(change =>
                change.Expected.Before.TypeArguments is [{ SpecialType: SpecialType.System_String }]
            );
    }
}
