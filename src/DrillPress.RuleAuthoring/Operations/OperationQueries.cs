using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

internal static class OperationQueries
{
    internal static CodeQuery<CodeNullCheck> NullChecks { get; } =
        NullCheckQueries.In(Sources.Nodes<ExpressionSyntax>());

    internal static CodeQuery<CodeOperation<IOperation>> All { get; } = OperationsIn(Sources.Files);

    internal static CodeQuery<CodeInvocation> Invocations { get; } = InvocationsIn(Sources.Files);

    internal static CodeQuery<CodeObjectCreation> ObjectCreations { get; } =
        Sources
            .Nodes<BaseObjectCreationExpressionSyntax>()
            .Select(node => new CodeObjectCreation(node.Source, node.Syntax));

    internal static CodeQuery<CodeOperation<T>> Of<T>()
        where T : IOperation => TypedRoot<T>.Query;

    internal static CodeQuery<CodeOperation<T>> InFiles<T>(CodeQuery<CodeFile> files)
        where T : IOperation =>
        OperationsIn(files)
            .Where(
                new RuleCondition<CodeOperation<IOperation>>(candidate => candidate.Operation is T)
            )
            .Select(candidate => new CodeOperation<T>(candidate.Source, (T)candidate.Operation));

    // Target-name constraints skip documents whose syntax cannot produce a matching call.
    internal static CodeQuery<CodeInvocation> InvocationsIn(CodeQuery<CodeFile> files) =>
        new((solution, names) => solution.SelectInvocations(files.In(solution), names));

    private static CodeQuery<CodeOperation<IOperation>> OperationsIn(CodeQuery<CodeFile> files) =>
        CodeQuery<CodeOperation<IOperation>>.Create(solution =>
            solution.SelectOperations(files.In(solution))
        );

    private static class TypedRoot<T>
        where T : IOperation
    {
        internal static readonly CodeQuery<CodeOperation<T>> Query = All.Where(
                new RuleCondition<CodeOperation<IOperation>>(candidate => candidate.Operation is T)
            )
            .Select(candidate => new CodeOperation<T>(candidate.Source, (T)candidate.Operation));
    }
}
