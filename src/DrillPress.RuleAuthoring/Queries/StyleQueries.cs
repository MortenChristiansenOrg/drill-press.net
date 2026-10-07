namespace DrillPress;

/// <summary>Declaration-part and statement selections for style conventions.</summary>
public static class StyleQueries
{
    /// <summary>Selects explicit local, foreach and out declarations whose inferred type and binding survive replacement with var.</summary>
    public static CodeQuery<CodeVariableDeclaration> WhereVarPreservesType(
        this CodeQuery<CodeVariableDeclaration> declarations
    ) => declarations.Where(declaration => declaration.CanUseVar);

    /// <summary>Selects written top-level declaration parts.</summary>
    public static CodeQuery<CodeTypeDeclaration> TopLevel(
        this CodeQuery<CodeTypeDeclaration> types
    ) => types.Where(type => type.IsTopLevel);

    /// <summary>Selects semantically explicit types, including an actual type named var.</summary>
    public static CodeQuery<CodeVariableDeclaration> WithExplicitType(
        this CodeQuery<CodeVariableDeclaration> declarations
    ) => declarations.Where(declaration => declaration.HasExplicitType);

    /// <summary>Selects local declaration groups with an initializer for every variable.</summary>
    public static CodeQuery<CodeVariableDeclaration> WithInitializer(
        this CodeQuery<CodeVariableDeclaration> declarations
    ) => declarations.Where(declaration => declaration.HasInitializer);

    /// <summary>Selects if statements with an else, including else-if continuations marked by Else.IsElseIf.</summary>
    public static CodeQuery<CodeIfStatement> WithElse(this CodeQuery<CodeIfStatement> statements) =>
        statements.Where(statement => statement.Else is not null);

    /// <summary>Selects the then and else branches of each statement; nested if statements contribute their own branches.</summary>
    public static CodeQuery<CodeBranch> Branches(this CodeQuery<CodeIfStatement> statements) =>
        statements.SelectMany(statement => statement.Branches);

    /// <summary>Selects branches that are not blocks. An else-if continuation is not a missing brace.</summary>
    public static CodeQuery<CodeBranch> WithoutBraces(this CodeQuery<CodeBranch> branches) =>
        branches.Where(branch => !branch.HasBraces && !branch.IsElseIf);
}
