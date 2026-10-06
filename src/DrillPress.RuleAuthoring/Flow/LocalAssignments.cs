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
        var scope =
            declaration
                .Ancestors()
                .FirstOrDefault(node =>
                    node is MemberDeclarationSyntax and not BaseTypeDeclarationSyntax
                )
            ?? source.Tree.GetRoot(source.Project.CancellationToken);
        var writes = _writes.GetOrAdd(scope, node => new(() => FindWrites(node))).Value;
        return !writes.IsResolved || writes.Writes.Contains(local)
            ? null
            : new(source, initializer);
    }

    private LocalAssignmentEvidence FindWrites(SyntaxNode scope)
    {
        var writes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (
            source
                .Model.GetDiagnostics(scope.Span, source.Project.CancellationToken)
                .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        )
            return new(false, writes);
        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            source.Project.CancellationToken.ThrowIfCancellationRequested();
            if (
                source.Model.GetOperation(identifier, source.Project.CancellationToken)
                    is ILocalReferenceOperation reference
                && IsWriteOrEscape(reference)
            )
                writes.Add(reference.Local);
        }
        return new(true, writes);
    }

    private static bool IsWriteOrEscape(ILocalReferenceOperation reference)
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
