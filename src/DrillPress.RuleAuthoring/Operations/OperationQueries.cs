using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

internal static class OperationQueries
{
    internal static CodeQuery<CodeNullCheck> NullChecks { get; } =
        NullCheckQueries.In(Sources.Nodes<ExpressionSyntax>());

    internal static CodeQuery<CodeOperation<IOperation>> All { get; } =
        Sources.Files.SelectMany(Discover);

    internal static CodeQuery<CodeInvocation> Invocations { get; } =
        Of<IInvocationOperation>()
            .Select(candidate => new CodeInvocation(candidate.Source, candidate.Operation));

    internal static CodeQuery<CodeObjectCreation> ObjectCreations { get; } =
        Sources
            .Nodes<BaseObjectCreationExpressionSyntax>()
            .Select(node => new CodeObjectCreation(node.Source, node.Syntax));

    internal static CodeQuery<CodeOperation<T>> Of<T>()
        where T : IOperation => TypedRoot<T>.Query;

    internal static CodeQuery<CodeOperation<T>> InFiles<T>(CodeQuery<CodeFile> files)
        where T : IOperation =>
        files
            .SelectMany(Discover)
            .Where(candidate => candidate.Operation is T)
            .Select(candidate => new CodeOperation<T>(candidate.Source, (T)candidate.Operation));

    internal static CodeQuery<CodeInvocation> InvocationsIn(CodeQuery<CodeFile> files) =>
        InFiles<IInvocationOperation>(files)
            .Select(candidate => new CodeInvocation(candidate.Source, candidate.Operation));

    private static IEnumerable<CodeOperation<IOperation>> Discover(CodeFile file)
    {
        var seen = new HashSet<IOperation>(ReferenceEqualityComparer.Instance);
        foreach (
            var syntax in file
                .Source.Tree.GetRoot(file.Source.Project.CancellationToken)
                .DescendantNodes()
                .Where(node =>
                    node
                        is BaseMethodDeclarationSyntax
                            or AccessorDeclarationSyntax
                            or EqualsValueClauseSyntax
                            or GlobalStatementSyntax
                            or ArrowExpressionClauseSyntax
                            or AttributeSyntax
                            or ConstructorInitializerSyntax
                )
        )
        {
            file.Source.Project.CancellationToken.ThrowIfCancellationRequested();
            var operation = file.Source.Model.GetOperation(
                syntax,
                file.Source.Project.CancellationToken
            );
            if (operation is null)
            {
                continue;
            }

            while (operation.Parent is { } parent)
            {
                operation = parent;
            }

            var pending = new Stack<IOperation>();
            pending.Push(operation);
            while (pending.TryPop(out var current))
            {
                file.Source.Project.CancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(current))
                {
                    continue;
                }

                yield return new(file.Source, current);
                foreach (var child in current.ChildOperations.Reverse())
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static class TypedRoot<T>
        where T : IOperation
    {
        internal static readonly CodeQuery<CodeOperation<T>> Query = All.Where(
                new RuleCondition<CodeOperation<IOperation>>(candidate => candidate.Operation is T)
            )
            .Select(candidate => new CodeOperation<T>(candidate.Source, (T)candidate.Operation));
    }
}
