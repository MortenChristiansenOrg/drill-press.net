using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.SampleRules.Fixes;

/// <summary>Supplies the framework-specific equivalence; the SDK validates source, surrounding binding and compiler-supplied arguments.</summary>
internal static class EmptyStringFix
{
    public static FixProposal? Create(MemberReference reference) =>
        reference.Source is { } source && reference.Syntax is { } expression
            ? Fix.For(source, expression)
                .ReplaceWith(
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal("")
                    )
                )
                .Propose(change =>
                    change.BeforeModel.GetSymbolInfo(change.Before).Symbol
                        is IFieldSymbol { Name: "Empty" } field
                    && CodeType.Of<string>().Matches(field.ContainingType)
                        ? ProofResult.Proven
                        : ProofResult.Unknown
                )
            : null;
}
