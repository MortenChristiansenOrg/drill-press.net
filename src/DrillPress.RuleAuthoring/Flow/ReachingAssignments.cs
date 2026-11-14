using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress;

// The assignments that can supply a read of a local or parameter, found by reaching definitions over the
// compiler's control flow graph. A value that is also written by reference, through a nested function or by
// a declaration form other than an ordinary declarator can't be followed.
internal sealed class ReachingAssignments(AnalysisSource source)
{
    // A parameter's value on entry, in the reaching set.
    private const int Entry = -1;

    private readonly ConcurrentDictionary<SyntaxNode, Lazy<ControlFlowGraph?>> _graphs = new();
    private readonly ConcurrentDictionary<(SyntaxNode, ISymbol), Lazy<VariableFlow?>> _flows =
        new();

    internal ReachingValues? At(IdentifierNameSyntax read, ISymbol symbol)
    {
        source.Project.CancellationToken.ThrowIfCancellationRequested();
        if (Resolve(symbol, read) is not { } variable)
            return null;
        var writes = source.LocalAssignments.WritesIn(variable.Scope);
        if (!writes.IsResolved)
            return null;
        if (!writes.Writes.Contains(symbol))
            return Unwritten(symbol, variable.Declaration);
        if (Function(read) != variable.Owner)
            return null;
        return _flows
            .GetOrAdd((variable.Owner, symbol), _ => new(() => Analyze(read, symbol, variable)))
            .Value?.At(read);
    }

    // A local declared by a type or property pattern holds the matched non-null value until something writes it.
    internal bool IsPatternMatched(ILocalSymbol local) =>
        local.DeclaringSyntaxReferences is [var reference]
        && reference.SyntaxTree == source.Tree
        && reference.GetSyntax(source.Project.CancellationToken)
            is SingleVariableDesignationSyntax
            {
                Parent: DeclarationPatternSyntax or RecursivePatternSyntax
            } designation
        && source.LocalAssignments.WritesIn(LocalReferences.Scope(source, designation))
            is { IsResolved: true } writes
        && !writes.Writes.Contains(local);

    private Variable? Resolve(ISymbol symbol, SyntaxNode read)
    {
        var cancellationToken = source.Project.CancellationToken;
        switch (symbol)
        {
            case ILocalSymbol { RefKind: RefKind.None } local
                when local.DeclaringSyntaxReferences is [var reference]
                    && reference.SyntaxTree == source.Tree
                    && reference.GetSyntax(cancellationToken)
                        is VariableDeclaratorSyntax
                        {
                            Parent.Parent: LocalDeclarationStatementSyntax or ForStatementSyntax
                        } declarator:
                return new(
                    declarator,
                    Function(declarator),
                    LocalReferences.Scope(source, declarator)
                );
            case IParameterSymbol { RefKind: RefKind.None } parameter
                when parameter.DeclaringSyntaxReferences is [var reference]:
                if (
                    reference.SyntaxTree != source.Tree
                    || reference.GetSyntax(cancellationToken) is not ParameterSyntax declaration
                )
                    return null;
                // Another part of a partial type can write a primary constructor parameter.
                if (
                    declaration.Parent?.Parent is TypeDeclarationSyntax
                    && parameter.ContainingType.DeclaringSyntaxReferences.Any(part =>
                        part.SyntaxTree != source.Tree
                    )
                )
                    return null;
                return new(
                    declaration,
                    Function(declaration),
                    LocalReferences.Scope(source, declaration)
                );
            // An implicit parameter, such as a setter's value, belongs to the function reading it.
            case IParameterSymbol { RefKind: RefKind.None, DeclaringSyntaxReferences: [] }:
                var owner = Function(read);
                return new(owner, owner, owner);
            default:
                return null;
        }
    }

    private static ReachingValues? Unwritten(ISymbol symbol, SyntaxNode declaration) =>
        symbol is IParameterSymbol ? new([], true)
        : declaration is VariableDeclaratorSyntax { Initializer.Value: { } initializer }
            ? new([initializer], false)
        : null;

    // The lambda, local function or member that runs a node; a primary constructor parameter belongs to its type.
    private SyntaxNode Function(SyntaxNode node) =>
        node.Ancestors()
            .FirstOrDefault(ancestor =>
                ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
            )
        ?? LocalReferences.Scope(source, node);

    private bool AllWritesTracked(ISymbol symbol, Variable variable)
    {
        foreach (var name in variable.Scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            source.Project.CancellationToken.ThrowIfCancellationRequested();
            if (
                name.Identifier.ValueText != symbol.Name
                || source.Model.GetOperation(name, source.Project.CancellationToken)
                    is not { } reference
                || !SymbolEqualityComparer.Default.Equals(Referenced(reference), symbol)
                || !LocalAssignments.IsWriteOrEscape(reference)
            )
                continue;
            var target = reference;
            while (target.Parent is IParenthesizedOperation)
                target = target.Parent;
            if (
                target.Parent is not ISimpleAssignmentOperation { IsRef: false } assignment
                || assignment.Target != target
                || Function(name) != variable.Owner
            )
                return false;
        }
        return true;
    }

    private static ISymbol? Referenced(IOperation operation) =>
        operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null,
        };

    private ControlFlowGraph? Graph(SyntaxNode read, SyntaxNode owner)
    {
        if (source.Model.GetOperation(read, source.Project.CancellationToken) is not { } root)
            return null;
        while (root.Parent is not null)
            root = root.Parent;
        var graph = _graphs.GetOrAdd(root.Syntax, _ => new(() => Create(root))).Value;
        foreach (
            var function in owner
                .AncestorsAndSelf()
                .Where(node =>
                    node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
                )
                .Reverse()
        )
            graph = graph is null ? null : Nested(graph, function);
        return graph;
    }

    private ControlFlowGraph? Create(IOperation root)
    {
        var cancellationToken = source.Project.CancellationToken;
        return root switch
        {
            IMethodBodyOperation body => ControlFlowGraph.Create(body, cancellationToken),
            IConstructorBodyOperation body => ControlFlowGraph.Create(body, cancellationToken),
            IBlockOperation block => ControlFlowGraph.Create(block, cancellationToken),
            IFieldInitializerOperation initializer => ControlFlowGraph.Create(
                initializer,
                cancellationToken
            ),
            IPropertyInitializerOperation initializer => ControlFlowGraph.Create(
                initializer,
                cancellationToken
            ),
            IParameterInitializerOperation initializer => ControlFlowGraph.Create(
                initializer,
                cancellationToken
            ),
            _ => null,
        };
    }

    private ControlFlowGraph? Nested(ControlFlowGraph graph, SyntaxNode function)
    {
        var cancellationToken = source.Project.CancellationToken;
        if (function is LocalFunctionStatementSyntax local)
            return
                source.Model.GetDeclaredSymbol(local, cancellationToken) is IMethodSymbol symbol
                && graph.LocalFunctions.Contains(symbol, SymbolEqualityComparer.Default)
                ? graph.GetLocalFunctionControlFlowGraph(symbol, cancellationToken)
                : null;
        var lambda = source.Model.GetSymbolInfo(function, cancellationToken).Symbol;
        return
            Operations(graph)
                .OfType<IFlowAnonymousFunctionOperation>()
                .FirstOrDefault(operation =>
                    SymbolEqualityComparer.Default.Equals(operation.Symbol, lambda)
                )
                is { } flow
            ? graph.GetAnonymousFunctionControlFlowGraph(flow, cancellationToken)
            : null;
    }

    private static IEnumerable<IOperation> Operations(ControlFlowGraph graph) =>
        graph.Blocks.SelectMany(Statements).SelectMany(operation => operation.DescendantsAndSelf());

    private static IEnumerable<IOperation> Statements(BasicBlock block) =>
        block.BranchValue is { } branch ? block.Operations.Append(branch) : block.Operations;

    private VariableFlow? Analyze(SyntaxNode read, ISymbol symbol, Variable variable)
    {
        if (!AllWritesTracked(symbol, variable) || Graph(read, variable.Owner) is not { } graph)
            return null;
        var assignments = new List<ISimpleAssignmentOperation>();
        var reads = new Dictionary<SyntaxNode, (int Block, int Statement)>();
        var generated = new int[graph.Blocks.Length][][];
        foreach (var block in graph.Blocks)
        {
            var statements = Statements(block).ToArray();
            generated[block.Ordinal] = new int[statements.Length][];
            for (var index = 0; index < statements.Length; index++)
            {
                generated[block.Ordinal][index] = Assignments(
                    statements[index],
                    symbol,
                    assignments
                );
                foreach (var operation in statements[index].DescendantsAndSelf())
                    if (SymbolEqualityComparer.Default.Equals(Referenced(operation), symbol))
                        reads.TryAdd(operation.Syntax, (block.Ordinal, index));
            }
        }
        return new(
            assignments,
            generated,
            Incoming(graph, symbol, generated, assignments.Count),
            reads
        );
    }

    // The assignments to a symbol within one statement, numbered in graph order.
    private static int[] Assignments(
        IOperation statement,
        ISymbol symbol,
        List<ISimpleAssignmentOperation> assignments
    )
    {
        var found = new List<int>();
        foreach (
            var assignment in statement.DescendantsAndSelf().OfType<ISimpleAssignmentOperation>()
        )
            if (SymbolEqualityComparer.Default.Equals(Referenced(assignment.Target), symbol))
            {
                found.Add(assignments.Count);
                assignments.Add(assignment);
            }
        return [.. found];
    }

    // A statement that assigns replaces every earlier definition; several assignments in one statement all reach.
    private static void Apply(HashSet<int> reaching, int[] generated)
    {
        if (generated.Length == 0)
            return;
        reaching.Clear();
        reaching.UnionWith(generated);
    }

    private HashSet<int>[] Incoming(
        ControlFlowGraph graph,
        ISymbol symbol,
        int[][][] generated,
        int count
    )
    {
        var everything = Enumerable.Range(0, count).ToHashSet();
        if (symbol is IParameterSymbol)
            everything.Add(Entry);
        var incoming = graph.Blocks.Select(_ => new HashSet<int>()).ToArray();
        var outgoing = graph.Blocks.Select(_ => new HashSet<int>()).ToArray();
        for (var changed = true; changed; )
        {
            source.Project.CancellationToken.ThrowIfCancellationRequested();
            changed = false;
            foreach (var block in graph.Blocks)
            {
                var into = new HashSet<int>();
                if (block.Kind == BasicBlockKind.Entry && symbol is IParameterSymbol)
                    into.Add(Entry);
                // Control reaches a handler from anywhere in its try, which the graph doesn't draw.
                if (StartsHandler(block))
                    into.UnionWith(everything);
                foreach (var branch in block.Predecessors)
                {
                    into.UnionWith(outgoing[branch.Source.Ordinal]);
                    foreach (var region in branch.FinallyRegions)
                        for (
                            var ordinal = region.FirstBlockOrdinal;
                            ordinal <= region.LastBlockOrdinal;
                            ordinal++
                        )
                            into.UnionWith(generated[ordinal].SelectMany(statement => statement));
                }
                var after = new HashSet<int>(into);
                foreach (var statement in generated[block.Ordinal])
                    Apply(after, statement);
                if (
                    !into.SetEquals(incoming[block.Ordinal])
                    || !after.SetEquals(outgoing[block.Ordinal])
                )
                {
                    incoming[block.Ordinal] = into;
                    outgoing[block.Ordinal] = after;
                    changed = true;
                }
            }
        }
        return incoming;
    }

    private static bool StartsHandler(BasicBlock block)
    {
        for (
            var region = block.EnclosingRegion;
            region is not null;
            region = region.EnclosingRegion
        )
            if (
                region.FirstBlockOrdinal == block.Ordinal
                && region.Kind
                    is ControlFlowRegionKind.Catch
                        or ControlFlowRegionKind.Filter
                        or ControlFlowRegionKind.Finally
            )
                return true;
        return false;
    }

    private sealed record Variable(SyntaxNode Declaration, SyntaxNode Owner, SyntaxNode Scope);

    // Reaching definitions of one variable within its function, computed once for all of its reads.
    private sealed class VariableFlow(
        List<ISimpleAssignmentOperation> assignments,
        int[][][] generated,
        HashSet<int>[] incoming,
        Dictionary<SyntaxNode, (int Block, int Statement)> reads
    )
    {
        internal ReachingValues? At(SyntaxNode read)
        {
            if (!reads.TryGetValue(read, out var position))
                return null;
            var statements = generated[position.Block];
            var reaching = new HashSet<int>(incoming[position.Block]);
            for (var earlier = 0; earlier < position.Statement; earlier++)
                Apply(reaching, statements[earlier]);
            // Assignments earlier in the reading statement itself, as in (x = Get()) != null && x.Ok.
            reaching.UnionWith(
                statements[position.Statement]
                    .Where(definition => assignments[definition].Syntax.Span.End <= read.SpanStart)
            );
            var values = new List<ExpressionSyntax>();
            foreach (var definition in reaching.Where(definition => definition != Entry).Order())
            {
                if (assignments[definition].Value.Syntax is not ExpressionSyntax value)
                    return null;
                values.Add(value);
            }
            return values.Count == 0 && !reaching.Contains(Entry)
                ? null
                : new(values.AsReadOnly(), reaching.Contains(Entry));
        }
    }
}
