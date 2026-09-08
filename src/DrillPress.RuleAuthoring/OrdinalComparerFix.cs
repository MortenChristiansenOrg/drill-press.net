using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

/// <summary>Removes the comparer only from Enumerable.Distinct&lt;string&gt;'s documented equivalent overload pair.</summary>
public static class OrdinalComparerFix
{
    /// <summary>Diagnoses Ordinal references inside arguments, including calls outside the fix allowlist.</summary>
    public static RuleCondition<MemberReference> IsArgument { get; } = new(reference =>
        reference.Syntax?.Ancestors().TakeWhile(node => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<ArgumentSyntax>().Any() == true);

    /// <summary>Proposes argument removal only after exact API, parameter mapping, and contextual binding checks.</summary>
    public static FixProposal? Create(MemberReference reference)
    {
        if (reference.Source is not { Document.IsEditable: true } source || reference.Syntax?.Ancestors()
            .OfType<ArgumentSyntax>().FirstOrDefault() is not { Parent: ArgumentListSyntax list } argument ||
            list.Parent is not InvocationExpressionSyntax call || !IsEligible(source, call, argument))
        {
            return null;
        }

        var rewritten = list.RemoveNode(argument, SyntaxRemoveOptions.KeepExteriorTrivia);
        if (rewritten is null)
        {
            return null;
        }

        var edit = new SourceEdit(source.Document.FileIdentity, source.Document.Fingerprint, list.SpanStart,
            list.Span.Length, list.ToString(), rewritten.ToString());
        return new([edit], project =>
        {
            var sources = project.Sources.Where(candidate => candidate.Document.FileIdentity == edit.FileIdentity).ToArray();
            return sources.Length > 0 && sources.All(candidate => Validate(candidate, edit));
        });
    }

    internal static bool IsDistinct(IMethodSymbol? method, int parameterCount)
    {
        method = method?.ReducedFrom is { } original ? original.Construct(method.TypeArguments.ToArray()) : method;
        return method is { Name: "Distinct", IsStatic: true, Arity: 1 } &&
            method.Parameters.Length == parameterCount && method.TypeArguments[0].SpecialType == SpecialType.System_String &&
            CodeType.MetadataNameOf(method.ContainingType) == "System.Linq.Enumerable" && CodeType.IsFrameworkSymbol(method.ContainingType) &&
            method.Parameters[0].Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T } &&
            (parameterCount == 1 || method.Parameters[1].Type is INamedTypeSymbol comparer &&
                CodeType.MetadataNameOf(comparer) == "System.Collections.Generic.IEqualityComparer`1");
    }

    private static bool IsEligible(AnalysisSource source, InvocationExpressionSyntax call, ArgumentSyntax argument)
    {
        if (ContextualRewrite.HasInteriorContent(argument) || ContextualRewrite.IsObservableSyntax(source, call) || source.Model.GetOperation(call) is not IInvocationOperation operation ||
            !IsDistinct(operation.TargetMethod, 2))
        {
            return false;
        }

        var mapped = operation.Arguments.FirstOrDefault(candidate => candidate.Syntax == argument);
        IOperation? value = mapped?.Value;
        while (value is IConversionOperation conversion && conversion.Conversion.IsImplicit && !conversion.Conversion.IsUserDefined)
        {
            value = conversion.Operand;
        }

        return mapped is { ArgumentKind: ArgumentKind.Explicit, Parameter.Name: "comparer" } &&
            value is IPropertyReferenceOperation { Property.Name: "Ordinal" } property &&
            CodeType.Of<StringComparer>().Matches(property.Property.ContainingType);
    }

    private static bool Validate(AnalysisSource source, SourceEdit edit)
    {
        if (!source.Document.IsEditable || source.Document.IsGenerated || source.Document.Fingerprint != edit.Fingerprint ||
            source.Tree.GetRoot().FindNode(new TextSpan(edit.Start, edit.Length), getInnermostNodeForTie: true) is not
                ArgumentListSyntax { Parent: InvocationExpressionSyntax call } list || list.ToString() != edit.OriginalText)
        {
            return false;
        }

        return list.Arguments.Any(argument => IsEligible(source, call, argument) &&
            list.RemoveNode(argument, SyntaxRemoveOptions.KeepExteriorTrivia)?.ToString() == edit.Replacement) &&
            ContextualRewrite.PreservesBinding(source, list, edit.Replacement, call);
    }
}
