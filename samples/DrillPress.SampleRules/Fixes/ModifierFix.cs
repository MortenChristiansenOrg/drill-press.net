using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.SampleRules.Fixes;

/// <summary>Conservative modifier removal, proved against the compiler's resulting declaration accessibility in every affected context.</summary>
internal static class ModifierFix
{
    /// <summary>Removes a sole internal or private access token from a named type, method or property only when its effective accessibility remains identical.</summary>
    public static FixProposal? RemoveRedundantAccessibility(
        CodeNode<MemberDeclarationSyntax> candidate
    )
    {
        var access = candidate
            .Syntax.ChildTokens()
            .Where(token =>
                token.Kind()
                    is SyntaxKind.PublicKeyword
                        or SyntaxKind.PrivateKeyword
                        or SyntaxKind.ProtectedKeyword
                        or SyntaxKind.InternalKeyword
                        or SyntaxKind.FileKeyword
            )
            .ToArray();
        if (
            access.Length != 1
            || access[0].Kind() is not (SyntaxKind.InternalKeyword or SyntaxKind.PrivateKeyword)
        )
            return null;
        return Fix.For(candidate)
            .RemoveModifier(access[0].Kind())
            .MustPreserve(Behavior.Accessibility | Behavior.Identity | Behavior.Contract)
            .SafeWhen(change => change.RemovedModifier is Modifier.Internal or Modifier.Private);
    }
}
