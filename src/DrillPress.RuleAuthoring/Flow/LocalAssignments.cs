using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

internal sealed class LocalAssignments(AnalysisSource source)
{
    private readonly ConcurrentDictionary<SyntaxNode, Lazy<LocalAssignmentEvidence>> _writes =
        new();

    internal CodeExpression? Initializer(ILocalSymbol local)
    {
        source.Project.CancellationToken.ThrowIfCancellationRequested();
        if (
            local.RefKind != RefKind.None
            || local.DeclaringSyntaxReferences is not [var reference]
            || reference.SyntaxTree != source.Tree
            || reference.GetSyntax(source.Project.CancellationToken)
                is not VariableDeclaratorSyntax { Initializer.Value: { } initializer } declaration
        )
            return null;
        var writes = WritesIn(LocalReferences.Scope(source, declaration));
        return !writes.IsResolved || writes.Writes.Contains(local)
            ? null
            : new(source, initializer);
    }

    // The locals and parameters written or passed by reference anywhere in a scope.
    internal LocalAssignmentEvidence WritesIn(SyntaxNode scope) =>
        _writes.GetOrAdd(scope, node => new(() => FindWrites(node))).Value;

    private LocalAssignmentEvidence FindWrites(SyntaxNode scope)
    {
        var writes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (source.Errors.In(scope.Span))
            return new(false, writes);
        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            source.Project.CancellationToken.ThrowIfCancellationRequested();
            var reference = source.Model.GetOperation(identifier, source.Project.CancellationToken);
            if (
                reference is ILocalReferenceOperation or IParameterReferenceOperation
                && IsWriteOrEscape(reference)
            )
                writes.Add(
                    reference is ILocalReferenceOperation local
                        ? local.Local
                        : ((IParameterReferenceOperation)reference).Parameter
                );
        }
        return new(true, writes);
    }

    internal static bool IsWriteOrEscape(IOperation reference)
    {
        if (
            reference
                .Syntax.Ancestors()
                .Any(node =>
                    node is RefExpressionSyntax or MakeRefExpressionSyntax
                    || node is ArgumentSyntax { RefOrOutKeyword.RawKind: not 0 }
                )
        )
            return true;
        IOperation value = reference;
        while (value.Parent is IParenthesizedOperation or IConversionOperation or ITupleOperation)
            value = value.Parent;
        return value.Parent switch
        {
            IAssignmentOperation assignment => assignment.Target == value,
            IIncrementOrDecrementOperation increment => increment.Target == value,
            IArgumentOperation argument => argument.Parameter?.RefKind != RefKind.None,
            IAddressOfOperation => true,
            _ => false,
        };
    }
}
