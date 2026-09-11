using DrillPress.Queries;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Operations;

/// <summary>Cached compiler-operation selections covering method bodies, accessors, initializers and top-level statements.</summary>
public static class OperationQueries
{
    /// <summary>Every operation in ordinary source, including implicit conversions and nested functions.</summary>
    public static CodeQuery<CodeOperation<IOperation>> All { get; } =
        Sources.Files.SelectMany(Discover);

    /// <summary>Resolved invocation operations, with parameter mapping and call-site semantics.</summary>
    public static CodeQuery<CodeInvocation> Invocations { get; } =
        Of<IInvocationOperation>()
            .Select(candidate => new CodeInvocation(candidate.Source, candidate.Operation));

    /// <summary>Selects a compiler operation kind without requiring consumers to traverse syntax or cast nodes.</summary>
    public static CodeQuery<CodeOperation<T>> Of<T>()
        where T : IOperation => TypedRoot<T>.Query;

    /// <summary>Discovers operations only in a selected file scope, avoiding binding unrelated projects.</summary>
    public static CodeQuery<CodeOperation<IOperation>> InFiles(CodeQuery<CodeFile> files) =>
        files.SelectMany(Discover);

    /// <summary>Discovers bound calls only within selected files.</summary>
    public static CodeQuery<CodeInvocation> InvocationsIn(CodeQuery<CodeFile> files) =>
        InFiles(files)
            .Where(candidate => candidate.Operation is IInvocationOperation)
            .Select(candidate => new CodeInvocation(
                candidate.Source,
                (IInvocationOperation)candidate.Operation
            ));

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
                new(candidate => candidate.Operation is T)
            )
            .Select(candidate => new CodeOperation<T>(candidate.Source, (T)candidate.Operation));
    }
}
