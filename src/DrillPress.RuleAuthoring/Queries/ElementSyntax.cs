using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

// The written syntax of an element, from which scope questions walk outwards.
internal static class ElementSyntax
{
    // Implemented by generic elements, whose syntax type a pattern can't name.
    internal interface IWritten
    {
        SyntaxNode Syntax { get; }
    }

    internal static SyntaxNode Of(ICodeElement element) =>
        element switch
        {
            CodeExpression expression => expression.Syntax,
            CodeInvocation call => call.Operation.Syntax,
            CodeObjectCreation creation => creation.Syntax,
            CodeCatch handler => handler.Syntax,
            CodeBranch branch => branch.Syntax,
            IWritten written => written.Syntax,
            _ => element
                .Source.Tree.GetRoot(element.Source.Project.CancellationToken)
                .FindNode(
                    new(element.Location.Start, element.Location.Length),
                    getInnermostNodeForTie: true
                ),
        };

    // Methods, accessors, local functions and lambdas run separately from the code around them.
    internal static bool IsFunction(SyntaxNode node) =>
        node
            is AnonymousFunctionExpressionSyntax
                or LocalFunctionStatementSyntax
                or BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax;
}
