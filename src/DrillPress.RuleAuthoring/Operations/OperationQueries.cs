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

    // Type-name constraints from Of(type) skip creations whose written type cannot name it.
    internal static CodeQuery<CodeObjectCreation> ObjectCreations { get; } =
        new(
            (solution, filter) => solution.SelectObjectCreations(Sources.Files.In(solution), filter)
        );

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
        new((solution, filter) => solution.SelectInvocations(files.In(solution), filter));

    private static CodeQuery<CodeOperation<IOperation>> OperationsIn(CodeQuery<CodeFile> files) =>
        new((solution, filter) => solution.SelectOperations(files.In(solution), filter));

    // A root of its own, so place filters skip documents before they are bound.
    private static class TypedRoot<T>
        where T : IOperation
    {
        internal static readonly CodeQuery<CodeOperation<T>> Query = new(
            (solution, filter) =>
                solution
                    .SelectOperations(Sources.Files.In(solution), filter)
                    .Where(candidate => candidate.Operation is T)
                    .Select(candidate => new CodeOperation<T>(
                        candidate.Source,
                        (T)candidate.Operation
                    ))
        );
    }
}
