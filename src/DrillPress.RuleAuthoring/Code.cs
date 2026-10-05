using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Root queries over a shared analysis; only selected projects supply candidates, and generated source is excluded.</summary>
public static class Code
{
    /// <summary>Creates typed primitive, enum or null literal syntax, preserving numeric types and escaping text.</summary>
    public static ExpressionSyntax Literal(object? value) => LiteralSyntax.Create(value);

    /// <summary>Parses a C# expression template with numbered original-input holes. Parentheses preserve precedence; repeated and unused inputs remain visible to evaluation proofs.</summary>
    public static ExpressionTemplate Expression(string template, params ExpressionInput[] inputs) =>
        ExpressionTemplates.Create(template, inputs);

    /// <summary>Constructs equality syntax from explicitly selected operands. This does not prove equivalence to a method call or authorize a rewrite.</summary>
    public static ExpressionTemplate Equal(ExpressionInput left, ExpressionInput right) =>
        Expression("{0} == {1}", left, right);

    /// <summary>Every ordinary physical file membership in selected projects.</summary>
    public static CodeQuery<CodeFile> Files => Sources.Files;

    /// <summary>Every selected evaluated project context.</summary>
    public static CodeQuery<AnalysisProject> Projects => Sources.Projects;

    /// <summary>Source syntax roots for rules that need compiler-specific shapes.</summary>
    public static CodeQuery<CodeNode<TSyntax>> Nodes<TSyntax>()
        where TSyntax : SyntaxNode => Sources.Nodes<TSyntax>();

    /// <summary>Bound calls across ordinary source, including accessors, initializers and nested functions.</summary>
    public static CodeQuery<CodeInvocation> Calls => OperationQueries.Invocations;

    /// <summary>Written foreach and await foreach occurrences, including deconstruction, with separate collection and advancement semantics.</summary>
    public static CodeQuery<CodeEnumeration> Enumerations { get; } =
        Nodes<CommonForEachStatementSyntax>()
            .Select(loop => new CodeEnumeration(loop.Source, loop.Syntax));

    /// <summary>Bound null checks retaining declaration and incoming flow evidence separately.</summary>
    public static CodeQuery<CodeNullCheck> NullChecks => OperationQueries.NullChecks;

    /// <summary>Written type parts, including every partial declaration and unresolved syntax.</summary>
    public static CodeQuery<CodeTypeDeclaration> TypeDeclarations { get; } =
        Sources
            .Nodes<MemberDeclarationSyntax>()
            .Where(node => node.Syntax is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            .Select(node => new CodeTypeDeclaration(node.Source, node.Syntax));

    /// <summary>Local declaration groups, including for/using declarations; multiple variables stay together.</summary>
    public static CodeQuery<CodeTypedDeclaration> LocalVariables { get; } =
        Sources
            .Nodes<VariableDeclarationSyntax>()
            .Where(node =>
                node.Syntax.Parent
                    is LocalDeclarationStatementSyntax
                        or ForStatementSyntax
                        or UsingStatementSyntax
            )
            .Select(node => new CodeTypedDeclaration(node.Source, node.Syntax, node.Syntax.Type));

    /// <summary>Ordinary foreach iteration-variable declarations; deconstruction remains available through Nodes.</summary>
    public static CodeQuery<CodeTypedDeclaration> ForEachLoops { get; } =
        Sources
            .Nodes<ForEachStatementSyntax>()
            .Select(node => new CodeTypedDeclaration(node.Source, node.Syntax, node.Syntax.Type));

    /// <summary>Out-variable declaration expressions, excluding unrelated deconstruction declarations.</summary>
    public static CodeQuery<CodeTypedDeclaration> OutVariables { get; } =
        Sources
            .Nodes<DeclarationExpressionSyntax>()
            .Where(node =>
                node.Syntax.Parent is ArgumentSyntax argument
                && argument.RefOrOutKeyword.IsKind(
                    Microsoft.CodeAnalysis.CSharp.SyntaxKind.OutKeyword
                )
            )
            .Select(node => new CodeTypedDeclaration(node.Source, node.Syntax, node.Syntax.Type));

    /// <summary>Written if statements and their existing branches.</summary>
    public static CodeQuery<CodeIfStatement> IfStatements { get; } =
        Sources
            .Nodes<IfStatementSyntax>()
            .Select(node => new CodeIfStatement(node.Source, node.Syntax));

    /// <summary>Statically marked xUnit v2/v3 method declarations, with inherited override markers.</summary>
    public static CodeQuery<CodeMethod> TestMethods => TestDiscovery.TestMethods;

    /// <summary>Concrete test-project classes declaring or inheriting xUnit tests.</summary>
    public static CodeQuery<CodeDeclaration> TestClasses => TestDiscovery.TestClasses;

    /// <summary>Selects resolved member expressions.</summary>
    public static CodeQuery<MemberReference> MemberReferences { get; } =
        new(
            (solution, names) =>
                solution
                    .SelectMemberReferences(names)
                    .Where(reference =>
                        reference.Source?.Project.Snapshot.IsAnalysisTarget != false
                    )
        );

    /// <summary>Selects ordinary source methods.</summary>
    public static CodeQuery<CodeMethod> Methods { get; } =
        new(solution =>
            solution.Methods.Where(method => method.Source.Project.Snapshot.IsAnalysisTarget)
        );

    /// <summary>Selects distinct ordinary named type definitions.</summary>
    public static CodeQuery<CodeDeclaration> Types { get; } =
        new(solution =>
            solution.Types.Where(type => type.Source.Project.Snapshot.IsAnalysisTarget)
        );

    /// <summary>Selects distinct interfaces, including partial and generic definitions.</summary>
    public static CodeQuery<CodeDeclaration> Interfaces { get; } =
        Types.Where(new(type => type.Symbol.TypeKind == TypeKind.Interface));
}
