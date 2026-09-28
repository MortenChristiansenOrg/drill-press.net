using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

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
        if (
            reference.Source is not { } source
            || reference.Syntax is not { } referenceSyntax
            || referenceSyntax.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault()
                is not { } call
            || source.Model.GetOperation(call) is not IInvocationOperation operation
            || !operation.Arguments.Any(argument =>
                argument.Parameter?.Name == "comparer"
                && argument.Value.Syntax.Span.Contains(referenceSyntax.Span)
            )
        )
            return null;
        return Fix.For(source, call)
            .RemoveArgument("comparer")
            .RequireTransition(
                new MethodTransition(
                    ResolvePair,
                    new Dictionary<string, string> { ["source"] = "source" }
                )
            )
            .RequireRemovedValue(change =>
                change.Removed.Operation.Syntax.Span.Contains(referenceSyntax.Span)
                    ? ApprovedDefault(change)
                    : ProofResult.Unknown
            )
            // StringComparer.Ordinal is the framework's immutable singleton. For this exact string
            // Distinct pair, omitting its getter evaluation has no observable contract effect.
            .RequireRemovedEvaluation(ApprovedDefault)
            .Propose(change =>
                IsDistinct(change.Expected.Before, 2) && IsDistinct(change.Expected.After, 1)
                    ? ProofResult.Proven
                    : ProofResult.Unknown
            );
    }

    private static MethodPair? ResolvePair(AnalysisProject project)
    {
        var type = project.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        if (type is null || !IsFrameworkEnumerable(type))
            return null;
        var methods = type.GetMembers("Distinct")
            .OfType<IMethodSymbol>()
            .Where(method => method.Arity == 1)
            .Select(method =>
                method.Construct(project.Compilation.GetSpecialType(SpecialType.System_String))
            )
            .ToArray();
        var before = methods.SingleOrDefault(method => IsDistinct(method, 2));
        var after = methods.SingleOrDefault(method => IsDistinct(method, 1));
        return before is null || after is null ? null : new(before, after);
    }

    private static ProofResult ApprovedDefault(ArgumentRemovalEvidence change)
    {
        var value = change.Removed.Operation;
        while (
            value
                is IConversionOperation
                {
                    Conversion.IsImplicit: true,
                    Conversion.IsUserDefined: false
                } conversion
        )
            value = conversion.Operand;
        return
            value
                is IPropertyReferenceOperation
                {
                    Property.Name: "Ordinal",
                    Property.IsStatic: true
                } property
            && CodeType.Of<StringComparer>().Matches(property.Property.ContainingType)
            && IsDistinct(change.Expected.Before, 2)
            && IsDistinct(change.Expected.After, 1)
            ? ProofResult.Proven
            : ProofResult.Unknown;
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
}
