using DrillPress;
using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Composable executable scopes sharing ordinary query lifetimes and generated-source boundaries.</summary>
public static class BodyQueries
{
    /// <summary>Selects existing ordinary method bodies; abstract and extern declarations contribute no scope.</summary>
    public static CodeQuery<CodeBody> Body(
        this CodeQuery<CodeMethod> methods,
        NestedFunctions nested = NestedFunctions.Exclude
    ) => methods.SelectMany(method => method.Body(nested) is { } body ? new[] { body } : []);

    /// <summary>Projects syntax once per contextual occurrence, including when input scopes overlap.</summary>
    public static CodeQuery<CodeNode<TSyntax>> Nodes<TSyntax>(this CodeQuery<CodeBody> bodies)
        where TSyntax : SyntaxNode =>
        CodeQuery<CodeNode<TSyntax>>.Create(solution =>
            bodies
                .In(solution)
                .SelectMany(body => body.Nodes<TSyntax>())
                .DistinctBy(node => (node.Source, node.Syntax.Span, node.Syntax.RawKind))
        );

    /// <summary>Selects resolved calls in the chosen scopes, honoring each scope's nested-function policy.</summary>
    public static CodeQuery<CodeInvocation> Calls(this CodeQuery<CodeBody> bodies) =>
        CodeQuery<CodeInvocation>.Create(solution =>
            bodies
                .In(solution)
                .SelectMany(body => body.Calls())
                .Where(call => call.IsResolved)
                .DistinctBy(call => (call.Source, call.Operation.Syntax.Span))
        );

    /// <summary>Selects adjustable control-flow syntax kinds, deduplicating overlapping scopes.</summary>
    public static CodeQuery<CodeNode<SyntaxNode>> ControlFlowNodes(
        this CodeQuery<CodeBody> bodies,
        ControlFlowKinds kinds
    ) =>
        CodeQuery<CodeNode<SyntaxNode>>.Create(solution =>
            bodies
                .In(solution)
                .SelectMany(body => body.ControlFlowNodes(kinds))
                .DistinctBy(node => (node.Source, node.Syntax.Span, node.Syntax.RawKind))
        );

    /// <summary>Selects nested functions as independent scopes with exclusion boundaries of their own.</summary>
    public static CodeQuery<CodeBody> NestedBodies(this CodeQuery<CodeBody> bodies) =>
        CodeQuery<CodeBody>.Create(solution =>
            bodies
                .In(solution)
                .SelectMany(body => body.NestedBodies())
                .DistinctBy(body => (body.Source, body.Root.Span))
        );
}
