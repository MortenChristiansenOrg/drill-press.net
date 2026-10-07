using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>The single entry point for selecting code. Every query covers ordinary source in the analyzed projects; generated source supplies semantics but never candidates.</summary>
/// <remarks>Queries are lazy and cached per analysis. Narrow them with fluent filters such as <c>InNonTestProjects()</c>, <c>Named(...)</c> or <c>Where(...)</c>, then register them with <c>rules.Rule(id, message).For(query)</c>.</remarks>
public static class Code
{
    /// <summary>Every ordinary C# document membership. Linked files and alternate target frameworks remain separate memberships.</summary>
    public static CodeQuery<CodeFile> Files => Sources.Files;

    /// <summary>Every document membership, including generated source, for semantic facts. Findings anchored to generated documents are suppressed.</summary>
    public static CodeQuery<CodeFile> FilesIncludingGenerated => Sources.FilesIncludingGenerated;

    /// <summary>Every analyzed evaluated project context; alternate target frameworks are separate projects.</summary>
    public static CodeQuery<AnalysisProject> Projects => Sources.Projects;

    /// <summary>Every ordinary and documentation comment, excluding comment-like text in strings and inactive preprocessor regions.</summary>
    public static CodeQuery<CodeComment> Comments => Roots.Comments;

    /// <summary>Distinct named type definitions (classes, structs, records, interfaces, enums and delegates); partial declarations count once per compilation.</summary>
    public static CodeQuery<CodeTypeDefinition> Types => Roots.Types;

    /// <summary>Distinct interface definitions, including partial and generic interfaces.</summary>
    public static CodeQuery<CodeTypeDefinition> Interfaces => Roots.Interfaces;

    /// <summary>Every written type declaration part, including each partial part and unresolved declarations. Use for modifiers, file-specific policies and edits.</summary>
    public static CodeQuery<CodeTypeDeclaration> TypeDeclarations => Roots.TypeDeclarations;

    /// <summary>Ordinary method declarations, including unresolved ones. Constructors, accessors and local functions are not methods.</summary>
    public static CodeQuery<CodeMethod> Methods => Roots.Methods;

    /// <summary>Written field variables; a declaration such as <c>int a, b;</c> yields one field per variable. Event fields are excluded.</summary>
    public static CodeQuery<CodeField> Fields => Roots.Fields;

    /// <summary>Written property declarations, excluding indexers.</summary>
    public static CodeQuery<CodeProperty> Properties => Roots.Properties;

    /// <summary>Written parameters of methods, constructors, operators, delegates, indexers, local functions, primary constructors and lambdas.</summary>
    public static CodeQuery<CodeParameter> Parameters => Roots.Parameters;

    /// <summary>Every resolved declared symbol, including events, accessors, locals and other kinds without a dedicated query. Partial declarations remain separate occurrences.</summary>
    public static CodeQuery<CodeSymbol> Declarations => SymbolQueries.Declarations;

    /// <summary>Test methods marked for xUnit v2/v3, NUnit or MSTest, including derived marker attributes and markers inherited by overrides. An inherited test is selected once, at its declaration.</summary>
    public static CodeQuery<CodeMethod> TestMethods => TestDiscovery.TestMethods;

    /// <summary>Concrete classes in test projects that declare or inherit test methods.</summary>
    public static CodeQuery<CodeTypeDefinition> TestClasses => TestDiscovery.TestClasses;

    /// <summary>Abstract classes in test projects that declare or inherit test methods; combine with <see cref="TestClasses"/> to include test bases.</summary>
    public static CodeQuery<CodeTypeDefinition> AbstractTestClasses =>
        TestDiscovery.AbstractTestClasses;

    /// <summary>Local declaration groups, including for and using declarations; <c>int a = 1, b = 2;</c> stays one candidate.</summary>
    public static CodeQuery<CodeVariableDeclaration> LocalVariables => Statements.LocalVariables;

    /// <summary>Ordinary foreach iteration-variable declarations; deconstruction loops remain available through <see cref="Enumerations"/>.</summary>
    public static CodeQuery<CodeVariableDeclaration> ForEachLoops => Statements.ForEachLoops;

    /// <summary>Out-variable declaration expressions such as <c>out int value</c>.</summary>
    public static CodeQuery<CodeVariableDeclaration> OutVariables => Statements.OutVariables;

    /// <summary>Written if statements, with their then and else branches.</summary>
    public static CodeQuery<CodeIfStatement> IfStatements => Statements.IfStatements;

    /// <summary>Written foreach and await foreach loops, including deconstruction, with separate collection and advancement semantics.</summary>
    public static CodeQuery<CodeEnumeration> Enumerations => Statements.Enumerations;

    /// <summary>Written catch clauses, with their caught type, filter and body.</summary>
    public static CodeQuery<CodeCatch> Catches => Statements.Catches;

    /// <summary>Bound method calls across ordinary source, including accessors, initializers and lambdas. Extension methods match in both instance and static spelling.</summary>
    public static CodeQuery<CodeInvocation> Calls => OperationQueries.Invocations;

    /// <summary>Object creations, including target-typed <c>new()</c>, with their bound type and constructor.</summary>
    public static CodeQuery<CodeObjectCreation> ObjectCreations => OperationQueries.ObjectCreations;

    /// <summary>Resolved field, property and method references, including aliases and static imports. Prefer <c>CodeType.Member(name).References</c> for one member.</summary>
    public static CodeQuery<MemberReference> MemberReferences => Roots.MemberReferences;

    /// <summary>Written references to named and keyword types, including qualifiers, attributes, casts, typeof and generic arguments. Prefer <c>CodeType.References</c> for one type.</summary>
    public static CodeQuery<CodeTypeReference> TypeReferences => Roots.TypeReferences;

    /// <summary>Recognized null tests (<c>is null</c>, <c>== null</c>, <c>HasValue</c> and their negations), with declaration and incoming flow evidence kept separate.</summary>
    public static CodeQuery<CodeNullCheck> NullChecks => OperationQueries.NullChecks;

    /// <summary>Resolved simple-name references to an exact symbol, matching source declarations across compilations by file identity and span.</summary>
    public static CodeQuery<CodeSymbol> ReferencesTo(ISymbol symbol) =>
        SymbolQueries.ReferencesTo(symbol);

    /// <summary>Raw Roslyn syntax nodes of any kind, for shapes without a dedicated query.</summary>
    public static CodeQuery<CodeNode<TSyntax>> Nodes<TSyntax>()
        where TSyntax : SyntaxNode => Sources.Nodes<TSyntax>();

    /// <summary>Raw Roslyn operations of any kind, including implicit operations, for shapes without a dedicated query.</summary>
    public static CodeQuery<CodeOperation<TOperation>> Operations<TOperation>()
        where TOperation : IOperation => OperationQueries.Of<TOperation>();

    // Initialized separately so that queries referencing one another observe a fixed order.
    private static class Roots
    {
        internal static readonly CodeQuery<CodeComment> Comments = Sources.Files.Comments();

        internal static readonly CodeQuery<CodeTypeDefinition> Types = new(solution =>
            solution.Types.Where(type => type.Source.Project.Snapshot.IsAnalysisTarget)
        );

        internal static readonly CodeQuery<CodeTypeDefinition> Interfaces = Types.Where(
            new RuleCondition<CodeTypeDefinition>(type =>
                type.Symbol.TypeKind == TypeKind.Interface
            )
        );

        internal static readonly CodeQuery<CodeTypeDeclaration> TypeDeclarations = Sources
            .Nodes<MemberDeclarationSyntax>()
            .Where(node => node.Syntax is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            .Select(node => new CodeTypeDeclaration(node.Source, node.Syntax));

        internal static readonly CodeQuery<CodeMethod> Methods = new(solution =>
            solution.Methods.Where(method => method.Source.Project.Snapshot.IsAnalysisTarget)
        );

        internal static readonly CodeQuery<CodeField> Fields = Sources
            .Nodes<VariableDeclaratorSyntax>()
            .Where(node => node.Syntax.Parent?.Parent is FieldDeclarationSyntax)
            .Select(node => new CodeField(node.Source, node.Syntax));

        internal static readonly CodeQuery<CodeProperty> Properties = Sources
            .Nodes<PropertyDeclarationSyntax>()
            .Select(node => new CodeProperty(node.Source, node.Syntax));

        internal static readonly CodeQuery<CodeParameter> Parameters = Sources
            .Nodes<ParameterSyntax>()
            .Where(node => !node.Syntax.Identifier.IsMissing)
            .Select(node => new CodeParameter(node.Source, node.Syntax));

        internal static readonly CodeQuery<MemberReference> MemberReferences = new(
            (solution, names) =>
                solution
                    .SelectMemberReferences(names)
                    .Where(reference => reference.Source.Project.Snapshot.IsAnalysisTarget)
        );

        internal static readonly CodeQuery<CodeTypeReference> TypeReferences = new(
            (solution, names) =>
                solution
                    .SelectTypeReferences(names)
                    .Where(reference => reference.Source.Project.Snapshot.IsAnalysisTarget)
        );
    }

    private static class Statements
    {
        internal static readonly CodeQuery<CodeVariableDeclaration> LocalVariables = Sources
            .Nodes<VariableDeclarationSyntax>()
            .Where(node =>
                node.Syntax.Parent
                    is LocalDeclarationStatementSyntax
                        or ForStatementSyntax
                        or UsingStatementSyntax
            )
            .Select(node => new CodeVariableDeclaration(
                node.Source,
                node.Syntax,
                node.Syntax.Type
            ));

        internal static readonly CodeQuery<CodeVariableDeclaration> ForEachLoops = Sources
            .Nodes<ForEachStatementSyntax>()
            .Select(node => new CodeVariableDeclaration(
                node.Source,
                node.Syntax,
                node.Syntax.Type
            ));

        internal static readonly CodeQuery<CodeVariableDeclaration> OutVariables = Sources
            .Nodes<DeclarationExpressionSyntax>()
            .Where(node =>
                node.Syntax.Parent is ArgumentSyntax argument
                && argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)
            )
            .Select(node => new CodeVariableDeclaration(
                node.Source,
                node.Syntax,
                node.Syntax.Type
            ));

        internal static readonly CodeQuery<CodeIfStatement> IfStatements = Sources
            .Nodes<IfStatementSyntax>()
            .Select(node => new CodeIfStatement(node.Source, node.Syntax));

        internal static readonly CodeQuery<CodeEnumeration> Enumerations = Sources
            .Nodes<CommonForEachStatementSyntax>()
            .Select(loop => new CodeEnumeration(loop.Source, loop.Syntax));

        internal static readonly CodeQuery<CodeCatch> Catches = Sources
            .Nodes<CatchClauseSyntax>()
            .Select(node => new CodeCatch(node.Source, node.Syntax));
    }
}
