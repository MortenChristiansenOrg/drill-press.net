using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Explicit declaration-part and statement selections for style conventions.</summary>
public static class StyleQueries
{
    /// <summary>Selects written top-level declaration parts.</summary>
    public static CodeQuery<CodeTypeDeclaration> TopLevel(
        this CodeQuery<CodeTypeDeclaration> types
    ) => types.Where(type => type.IsTopLevel);

    /// <summary>Selects actual modifier tokens on each declaration part.</summary>
    public static CodeQuery<CodeTypeDeclaration> WithExplicitModifier(
        this CodeQuery<CodeTypeDeclaration> types,
        Modifier modifier
    ) => types.Where(type => type.HasExplicitModifier(modifier));

    /// <summary>Selects semantically explicit types, including an actual type named var.</summary>
    public static CodeQuery<CodeTypedDeclaration> WithExplicitType(
        this CodeQuery<CodeTypedDeclaration> declarations
    ) => declarations.Where(declaration => declaration.HasExplicitType);

    /// <summary>Selects local declaration groups with an initializer for every variable.</summary>
    public static CodeQuery<CodeTypedDeclaration> WithInitializer(
        this CodeQuery<CodeTypedDeclaration> declarations
    ) => declarations.Where(declaration => declaration.HasInitializer);

    /// <summary>Selects if statements with an else, including else-if continuations marked by Else.IsElseIf.</summary>
    public static CodeQuery<CodeIfStatement> WithElse(this CodeQuery<CodeIfStatement> statements) =>
        statements.Where(statement => statement.Else is not null);
}
