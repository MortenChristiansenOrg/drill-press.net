using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Starts a safe correction for a selected element. Every chain ends with <c>Propose()</c>, when the library owns the equivalence proof, or <c>SafeWhen(...)</c>, when you state why the change preserves behavior.</summary>
/// <remarks>Selecting an element never authorizes an edit. Builders withhold corrections in generated or non-editable source, around interior comments or directives, inside nameof or expression trees, and whenever any affected compilation fails to compile or rebinds differently. A withheld fix keeps the finding.</remarks>
public static class Fix
{
    /// <summary>Starts a replacement of an expression.</summary>
    public static ExpressionFix For(CodeExpression expression) =>
        new(expression.Source, expression.Syntax);

    /// <summary>Starts a replacement of a member reference such as <c>string.Empty</c>.</summary>
    public static ExpressionFix For(MemberReference reference) =>
        new(reference.Source, reference.Syntax);

    /// <summary>Starts a replacement of an object creation.</summary>
    public static ExpressionFix For(CodeObjectCreation creation) =>
        new(creation.Source, creation.Syntax);

    /// <summary>Starts a replacement of a call, or the removal of one of its arguments.</summary>
    public static InvocationFix For(CodeInvocation call) =>
        new(call.Source, (ExpressionSyntax)call.Operation.Syntax);

    /// <summary>Starts the removal of an explicit argument.</summary>
    public static ArgumentFix For(CodeArgument argument) => new(argument);

    /// <summary>Starts a modifier edit on a written declaration: a type part, method, field, property or parameter.</summary>
    public static DeclarationFix For(ICodeDeclaration declaration) => new(declaration);

    /// <summary>Starts an edit of an if or else branch.</summary>
    public static BranchFix For(CodeBranch branch) => new(branch);

    /// <summary>Starts the removal of a comment.</summary>
    public static CommentFix For(CodeComment comment) => new(comment);

    /// <summary>Starts an edit of a local, foreach or out variable's written type.</summary>
    public static VariableDeclarationFix For(CodeVariableDeclaration declaration) =>
        new(declaration);

    /// <summary>Starts extracting every occurrence of a grouped expression into one private constant or helper method.</summary>
    public static ExpressionExtraction Extract(ExpressionGroup group) => new(group);
}
