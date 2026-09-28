using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

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

    /// <summary>Proposes argument removal only after exact API, parameter mapping, and contextual binding checks.</summary>
    public static FixProposal? Create(MemberReference reference)
    {
        if (
            reference.Source is not { Document.IsEditable: true } source
            || reference.Syntax?.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault()
                is not { Parent: ArgumentListSyntax list } argument
            || list.Parent is not InvocationExpressionSyntax call
            || !IsEligible(source, call, argument)
        )
        {
            return null;
        }

        var rewritten = list.RemoveNode(argument, SyntaxRemoveOptions.KeepExteriorTrivia);
        if (rewritten is null)
        {
            return null;
        }

        var edit = new SourceEdit(
            source.Document.FileIdentity,
            source.Document.Fingerprint,
            list.SpanStart,
            list.Span.Length,
            list.ToString(),
            rewritten.ToString()
        );
        return SourceChanges.Propose(
            [edit],
            context =>
                context
                    .Original.Sources.Where(candidate =>
                        candidate.Document.FileIdentity == edit.FileIdentity
                    )
                    .All(candidate => Validate(candidate, edit, context))
        );
    }

    internal static bool IsDistinct(IMethodSymbol? method, int parameterCount)
    {
        method = method?.ReducedFrom is { } original
            ? original.Construct(method.TypeArguments.ToArray())
            : method;
        return method is { Name: "Distinct", IsStatic: true, Arity: 1 }
            && method.Parameters.Length == parameterCount
            && method.TypeArguments[0].SpecialType == SpecialType.System_String
            && IsFrameworkEnumerable(method.ContainingType)
            && method.Parameters[0].Type
                is INamedTypeSymbol
                {
                    OriginalDefinition.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T
                }
            && (
                parameterCount == 1
                || method.Parameters[1].Type is INamedTypeSymbol comparer
                    && CodeType
                        .Named("System.Collections.Generic.IEqualityComparer<>")
                        .Matches(comparer)
            );
    }

    private static bool IsFrameworkEnumerable(INamedTypeSymbol type)
    {
        var assembly = type.ContainingAssembly.Identity;
        return CodeType.Named("System.Linq.Enumerable").Matches(type)
            && (
                assembly.Name is "mscorlib" or "netstandard" or "System"
                || assembly.Name.StartsWith("System.", StringComparison.Ordinal)
            )
            && Convert.ToHexString(assembly.PublicKeyToken.AsSpan())
                is "B03F5F7F11D50A3A"
                    or "B77A5C561934E089"
                    or "7CEC85D7BEA7798E";
    }

    private static bool IsEligible(
        AnalysisSource source,
        InvocationExpressionSyntax call,
        ArgumentSyntax argument
    )
    {
        if (
            RewriteSyntax.HasInteriorContent(argument)
            || RewriteSyntax.IsObservableSyntax(source, call)
            || source.Model.GetOperation(call) is not IInvocationOperation operation
            || !IsDistinct(operation.TargetMethod, 2)
        )
        {
            return false;
        }

        var mapped = operation.Arguments.FirstOrDefault(candidate => candidate.Syntax == argument);
        IOperation? value = mapped?.Value;
        while (
            value is IConversionOperation conversion
            && conversion.Conversion.IsImplicit
            && !conversion.Conversion.IsUserDefined
        )
        {
            value = conversion.Operand;
        }

        return mapped is { ArgumentKind: ArgumentKind.Explicit, Parameter.Name: "comparer" }
            && value is IPropertyReferenceOperation { Property.Name: "Ordinal" } property
            && CodeType.Of<StringComparer>().Matches(property.Property.ContainingType);
    }

    private static bool Validate(AnalysisSource source, SourceEdit edit, RewriteContext context)
    {
        if (
            !source.Document.IsEditable
            || source.Document.IsGenerated
            || source.Document.Fingerprint != edit.Fingerprint
            || source
                .Tree.GetRoot()
                .FindNode(new TextSpan(edit.Start, edit.Length), getInnermostNodeForTie: true)
                is not ArgumentListSyntax { Parent: InvocationExpressionSyntax call } list
            || list.ToString() != edit.OriginalText
        )
        {
            return false;
        }

        return list.Arguments.Any(argument =>
                IsEligible(source, call, argument)
                && list.RemoveNode(argument, SyntaxRemoveOptions.KeepExteriorTrivia)?.ToString()
                    == edit.Replacement
            ) && PreservesCall(source, call, edit, context);
    }

    private static bool PreservesCall(
        AnalysisSource source,
        InvocationExpressionSyntax call,
        SourceEdit edit,
        RewriteContext context
    )
    {
        if (
            context.Evidence(source, call)
                is not { After: InvocationExpressionSyntax rewritten } evidence
            || !IsDistinct(evidence.AfterModel.GetSymbolInfo(rewritten).Symbol as IMethodSymbol, 1)
        )
            return false;

        // This policy proves the overload pair. The shared proof checks the complete
        // invocation's type/conversion and every enclosing expression's binding.
        return RewriteChecks.SameEnclosingBindings(evidence) == ProofResult.Proven
            && RewriteChecks.SameCompilerSuppliedArguments(evidence) == ProofResult.Proven;
    }
}
