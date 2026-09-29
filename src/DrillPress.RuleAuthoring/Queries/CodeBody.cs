using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

/// <summary>An explicitly scoped executable body. Syntax discovery does not imply reachability or guaranteed execution.</summary>
public sealed class CodeBody
{
    /// <summary>Tests for a resolved call in this body's configured nested-function scope.</summary>
    public bool Calls(CodeMember member, Func<CodeInvocation, bool>? where = null) =>
        Invocations()
            .Any(call => call.IsResolved && call.Calls(member) && (where?.Invoke(call) ?? true));

    /// <summary>Tests whether this scope passes a typed compiler constant to the specified method parameter.</summary>
    public bool Calls<T>(CodeMember member, string withArgument, T equalTo) =>
        Calls(
            member,
            call => call.ArgumentsFor(withArgument).Any(argument => argument.Is(equalTo))
        );

    internal CodeBody(AnalysisSource source, SyntaxNode root, NestedFunctions nested)
    {
        Source = source;
        Root = root;
        Nested = nested;
    }

    /// <summary>The original compilation membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>The body block or arrow expression, excluding method attributes and defaults.</summary>
    public SyntaxNode Root { get; }

    /// <summary>The explicit boundary policy for this scope.</summary>
    public NestedFunctions Nested { get; }

    /// <summary>Enumerates original nodes once in source order; nested function syntax remains a boundary node when excluded.</summary>
    public IEnumerable<CodeNode<TSyntax>> Nodes<TSyntax>()
        where TSyntax : SyntaxNode
    {
        var pending = new Stack<SyntaxNode>();
        pending.Push(Root);
        while (pending.TryPop(out var node))
        {
            Source.Project.CancellationToken.ThrowIfCancellationRequested();
            if (node is TSyntax syntax)
                yield return new(Source, syntax);
            if (IsFunction(node))
            {
                if (Nested == NestedFunctions.Include && BodyOf(node) is { } body)
                    pending.Push(body);
                continue;
            }
            foreach (var child in node.ChildNodes().Reverse())
                pending.Push(child);
        }
    }

    /// <summary>Bound source calls in this body, without a solution-wide operation scan.</summary>
    public IEnumerable<CodeInvocation> Invocations() =>
        Nodes<InvocationExpressionSyntax>()
            .SelectMany(node =>
                node.Operation is IInvocationOperation call
                    ? new[] { new CodeInvocation(Source, call) }
                    : []
            );

    /// <summary>Finds selected syntax categories, including an expression-bodied method's root expression.</summary>
    public IEnumerable<CodeNode<SyntaxNode>> ControlFlowNodes(ControlFlowKinds kinds) =>
        Nodes<SyntaxNode>().Where(node => (Kind(node.Syntax) & kinds) != 0);

    /// <summary>Returns independently executable descendants with their own nested-function exclusion boundary.</summary>
    public IEnumerable<CodeBody> NestedBodies() =>
        Root.DescendantNodesAndSelf()
            .Where(IsFunction)
            .Select(BodyOf)
            .OfType<SyntaxNode>()
            .Select(node => new CodeBody(Source, node, NestedFunctions.Exclude));

    internal static bool IsFunction(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    private static SyntaxNode? BodyOf(SyntaxNode node) =>
        node switch
        {
            LambdaExpressionSyntax lambda => lambda.Body,
            AnonymousMethodExpressionSyntax method => method.Block,
            LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body
                ?? local.ExpressionBody?.Expression,
            _ => null,
        };

    private static ControlFlowKinds Kind(SyntaxNode node) =>
        node switch
        {
            IfStatementSyntax => ControlFlowKinds.If,
            SwitchStatementSyntax => ControlFlowKinds.SwitchStatement,
            SwitchExpressionSyntax => ControlFlowKinds.SwitchExpression,
            ForStatementSyntax
            or CommonForEachStatementSyntax
            or WhileStatementSyntax
            or DoStatementSyntax => ControlFlowKinds.Loop,
            ConditionalExpressionSyntax => ControlFlowKinds.ConditionalExpression,
            CatchFilterClauseSyntax => ControlFlowKinds.CatchFilter,
            BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.LogicalAndExpression)
                    || binary.IsKind(SyntaxKind.LogicalOrExpression) =>
                ControlFlowKinds.ShortCircuit,
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression) =>
                ControlFlowKinds.Coalesce,
            ConditionalAccessExpressionSyntax => ControlFlowKinds.ConditionalAccess,
            _ => ControlFlowKinds.None,
        };
}
