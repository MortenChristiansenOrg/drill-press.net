using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>Binds one document's operations once for every query and rule, and lists the call target names its syntax can produce without binding it.</summary>
internal sealed class SourceOperations
{
    // Compiler-generated calls whose target names need not be written: query clauses, collection
    // initializer Add, interpolated string handler appends, constructor initializers (including the
    // implicit base() call) and delegate invocations, whose target is Invoke.
    private static readonly string[] _queryMethods =
    [
        "Select",
        "SelectMany",
        "Where",
        "Join",
        "GroupJoin",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "GroupBy",
        "Cast",
    ];
    private static readonly string[] _interpolationHandlerMethods =
    [
        "AppendLiteral",
        "AppendFormatted",
    ];

    private readonly AnalysisSource _source;
    private readonly Lazy<IReadOnlyList<IOperation>> _all;
    private readonly Lazy<IReadOnlyList<CodeInvocation>> _invocations;
    private readonly Lazy<IReadOnlySet<string>> _invocationNames;

    internal SourceOperations(AnalysisSource source)
    {
        _source = source;
        _all = new(() =>
        {
            using var scope = source.Project.MeasureShared("operations");
            return Discover();
        });
        _invocations = new(() =>
            Array.AsReadOnly(
                All.OfType<IInvocationOperation>()
                    .Select(operation => new CodeInvocation(source, operation))
                    .ToArray()
            )
        );
        _invocationNames = new(ReadInvocationNames);
    }

    /// <summary>Every operation once, depth first from each executable root in syntax order, including implicit operations.</summary>
    internal IReadOnlyList<IOperation> All => _all.Value;

    internal IReadOnlyList<CodeInvocation> Invocations => _invocations.Value;

    /// <summary>A superset of the target names of <see cref="Invocations"/>: written identifiers plus the names of compiler-generated calls the syntax can imply.</summary>
    internal IReadOnlySet<string> InvocationNames => _invocationNames.Value;

    internal bool IsBound => _all.IsValueCreated;

    /// <summary>Binds ahead of ordered consumption. A failure stays cached and surfaces when the consumer reads <see cref="All"/>, in source order.</summary>
    internal void Prepare()
    {
        try
        {
            _ = _all.Value;
        }
        catch (Exception)
        {
            // Observed again, deterministically, by the ordered reader.
        }
    }

    private IReadOnlyList<IOperation> Discover()
    {
        var token = _source.Project.CancellationToken;
        var seen = new HashSet<IOperation>(ReferenceEqualityComparer.Instance);
        var result = new List<IOperation>();
        foreach (
            var syntax in _source
                .Tree.GetRoot(token)
                .DescendantNodesAndSelf()
                .Where(node =>
                    node
                        is CompilationUnitSyntax { Members: [GlobalStatementSyntax, ..] }
                            or BaseMethodDeclarationSyntax
                            or AccessorDeclarationSyntax
                            or EqualsValueClauseSyntax
                            or GlobalStatementSyntax
                            or ArrowExpressionClauseSyntax
                            or AttributeSyntax
                            or ConstructorInitializerSyntax
                )
        )
        {
            token.ThrowIfCancellationRequested();
            var operation = _source.Model.GetOperation(syntax, token);
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
                token.ThrowIfCancellationRequested();
                if (!seen.Add(current))
                {
                    continue;
                }

                result.Add(current);
                foreach (var child in current.ChildOperations.Reverse())
                {
                    pending.Push(child);
                }
            }
        }

        return result.AsReadOnly();
    }

    private IReadOnlySet<string> ReadInvocationNames()
    {
        var names = new HashSet<string> { WellKnownMemberNames.InstanceConstructorName };
        foreach (
            var item in _source
                .Tree.GetRoot(_source.Project.CancellationToken)
                .DescendantNodesAndTokens()
        )
        {
            if (item.IsToken)
            {
                if (item.IsKind(SyntaxKind.IdentifierToken))
                    names.Add(item.AsToken().ValueText);
                continue;
            }

            switch (item.AsNode())
            {
                case InvocationExpressionSyntax:
                    names.Add(WellKnownMemberNames.DelegateInvokeName);
                    break;
                case InitializerExpressionSyntax or CollectionExpressionSyntax:
                    names.Add(WellKnownMemberNames.CollectionInitializerAddMethodName);
                    names.Add(WellKnownMemberNames.DelegateInvokeName);
                    break;
                case QueryExpressionSyntax:
                    names.UnionWith(_queryMethods);
                    names.Add(WellKnownMemberNames.DelegateInvokeName);
                    break;
                case InterpolatedStringExpressionSyntax:
                    names.UnionWith(_interpolationHandlerMethods);
                    names.Add(WellKnownMemberNames.DelegateInvokeName);
                    break;
            }
        }

        return names;
    }
}
