using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

// GetDeclaredSymbol only accepts declaration shapes. Names that Roslyn synthesizes or
// qualifies retain binding; a written identifier is only a necessary precondition.
internal static class DeclarationCandidates
{
    internal static bool Includes(SyntaxNode syntax, NameFilter? names) =>
        CanDeclare(syntax)
        && (names is null || WrittenName(syntax) is not { } name || names.Accepts(name));

    private static bool CanDeclare(SyntaxNode syntax) =>
        syntax
            is MemberDeclarationSyntax
                or AccessorDeclarationSyntax
                or QueryClauseSyntax
                or LocalFunctionStatementSyntax
                or LabeledStatementSyntax
                or SwitchLabelSyntax
                or AnonymousObjectCreationExpressionSyntax
                or AnonymousObjectMemberDeclaratorSyntax
                or TupleExpressionSyntax
                or ArgumentSyntax { Parent: TupleExpressionSyntax }
                or VariableDeclaratorSyntax
                or SingleVariableDesignationSyntax
                or TupleElementSyntax
                or ParameterSyntax
                or TypeParameterSyntax
                or UsingDirectiveSyntax { Alias: not null }
                or ExternAliasDirectiveSyntax
                or ForEachStatementSyntax
                or CatchDeclarationSyntax
                or JoinIntoClauseSyntax
                or QueryContinuationSyntax
                or CompilationUnitSyntax;

    private static string? WrittenName(SyntaxNode syntax) =>
        syntax switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Identifier.ValueText,
            DelegateDeclarationSyntax declaration => declaration.Identifier.ValueText,
            MethodDeclarationSyntax { ExplicitInterfaceSpecifier: null } declaration => declaration
                .Identifier
                .ValueText,
            PropertyDeclarationSyntax { ExplicitInterfaceSpecifier: null } declaration =>
                declaration.Identifier.ValueText,
            EventDeclarationSyntax { ExplicitInterfaceSpecifier: null } declaration => declaration
                .Identifier
                .ValueText,
            EnumMemberDeclarationSyntax declaration => declaration.Identifier.ValueText,
            VariableDeclaratorSyntax declaration => declaration.Identifier.ValueText,
            ParameterSyntax declaration => declaration.Identifier.ValueText,
            TypeParameterSyntax declaration => declaration.Identifier.ValueText,
            LocalFunctionStatementSyntax declaration => declaration.Identifier.ValueText,
            SingleVariableDesignationSyntax declaration => declaration.Identifier.ValueText,
            LabeledStatementSyntax declaration => declaration.Identifier.ValueText,
            ForEachStatementSyntax declaration => declaration.Identifier.ValueText,
            CatchDeclarationSyntax declaration => declaration.Identifier.ValueText,
            UsingDirectiveSyntax { Alias: { } alias } => alias.Name.Identifier.ValueText,
            ExternAliasDirectiveSyntax declaration => declaration.Identifier.ValueText,
            FromClauseSyntax declaration => declaration.Identifier.ValueText,
            LetClauseSyntax declaration => declaration.Identifier.ValueText,
            JoinClauseSyntax declaration => declaration.Identifier.ValueText,
            JoinIntoClauseSyntax declaration => declaration.Identifier.ValueText,
            QueryContinuationSyntax declaration => declaration.Identifier.ValueText,
            _ => null,
        };
}
