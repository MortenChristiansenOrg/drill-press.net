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
        Calls().Any(call => call.Calls(member) && call.IsResolved && (where?.Invoke(call) ?? true));

    /// <summary>Tests whether this scope passes a typed compiler constant to the specified method parameter.</summary>
    public bool Calls<T>(CodeMember member, string withArgument, T equalTo) =>
        Calls(
            member,
            call => call.ArgumentsFor(withArgument).Any(argument => argument.Is(equalTo))
        );

    /// <summary>Scopes body queries to any original syntax node, such as one statement, a loop body or a lambda body.</summary>
    /// <exception cref="ArgumentException">The node does not belong to the source's syntax tree.</exception>
    public static CodeBody Of(
        AnalysisSource source,
        SyntaxNode root,
        NestedFunctions nested = NestedFunctions.Exclude
    ) =>
        root.SyntaxTree == source.Tree
            ? new(source, root, nested)
            : throw new ArgumentException(
                "The node must belong to the source's original syntax tree.",
                nameof(root)
            );

    internal CodeBody(AnalysisSource source, SyntaxNode root, NestedFunctions nested)
    {
        Source = source;
        Root = root;
        Nested = nested;
    }

    /// <summary>The original compilation membership.</summary>
    public AnalysisSource Source { get; }

    /// <summary>Whether the body is an empty block; comments do not count as statements.</summary>
    public bool IsEmpty => Root is BlockSyntax { Statements.Count: 0 };

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
    public IEnumerable<CodeInvocation> Calls() =>
        Nodes<InvocationExpressionSyntax>()
            .SelectMany(node =>
                node.Operation is IInvocationOperation call
                    ? new[] { new CodeInvocation(Source, call) }
                    : []
            );

    /// <summary>The block's statements in sections separated by blank lines, as in a test's Arrange, Act and Assert; empty for an expression body.</summary>
    /// <remarks>A blank line is a whitespace-only line between two statements. Comment lines do not separate sections, and neither do blank lines inside a statement, a comment or disabled code.</remarks>
    public IReadOnlyList<CodeSection> Sections()
    {
        if (Root is not BlockSyntax block)
            return [];
        var sections = new List<List<StatementSyntax>>();
        StatementSyntax? previous = null;
        foreach (var statement in block.Statements)
        {
            if (previous is null || HasBlankLineBetween(previous, statement))
                sections.Add([]);
            sections[^1].Add(statement);
            previous = statement;
        }
        return sections
            .Select((statements, index) => new CodeSection(Source, statements, index, Nested))
            .ToArray();
    }

    private bool HasBlankLineBetween(StatementSyntax previous, StatementSyntax next)
    {
        var text = Source.Tree.GetText(Source.Project.CancellationToken);
        var root = Source.Tree.GetRoot(Source.Project.CancellationToken);
        var first = text.Lines.GetLineFromPosition(previous.Span.End).LineNumber + 1;
        var last = text.Lines.GetLineFromPosition(next.SpanStart).LineNumber - 1;
        for (var number = first; number <= last; number++)
        {
            var line = text.Lines[number];
            if (
                string.IsNullOrWhiteSpace(text.ToString(line.Span))
                && root.FindTrivia(line.Start, findInsideTrivia: true).Kind()
                    is SyntaxKind.WhitespaceTrivia
                        or SyntaxKind.EndOfLineTrivia
            )
                return true;
        }
        return false;
    }

    /// <summary>The whitespace-only lines inside the block, as between a test's Arrange, Act and Assert sections; zero for an expression body.</summary>
    /// <remarks>Lines inside a token, such as a multi-line string, don't count, and neither do lines in lambdas and local functions unless the body includes nested functions.</remarks>
    public int BlankLines
    {
        get
        {
            if (Root is not BlockSyntax block)
                return 0;
            var text = Source.Tree.GetText(Source.Project.CancellationToken);
            var first =
                text.Lines.GetLineFromPosition(block.OpenBraceToken.Span.End).LineNumber + 1;
            var last =
                text.Lines.GetLineFromPosition(block.CloseBraceToken.SpanStart).LineNumber - 1;
            var functions =
                Nested == NestedFunctions.Include
                    ? []
                    : block.DescendantNodes().Where(IsFunction).Select(node => node.Span).ToList();
            var count = 0;
            for (var number = first; number <= last; number++)
            {
                var span = text.Lines[number].Span;
                if (
                    string.IsNullOrWhiteSpace(text.ToString(span))
                    && !IsInsideToken(block, span.Start)
                    && !functions.Any(function => function.Contains(span.Start))
                )
                    count++;
            }
            return count;
        }
    }

    private static bool IsInsideToken(SyntaxNode block, int position)
    {
        var token = block.FindToken(position);
        return token.Span.Start < position && position < token.Span.End;
    }

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
