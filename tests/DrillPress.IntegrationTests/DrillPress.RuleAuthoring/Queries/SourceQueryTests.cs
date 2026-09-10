using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Queries;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class SourceQueryTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Error_types_do_not_match_configured_semantic_identities()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { Missing field; }")], allowErrors: true);
        var node = Sources.Nodes<VariableDeclarationSyntax>().In(workspace.Analyze(TestContext.Current.CancellationToken)).Single();
        var type = node.Source.Model.GetTypeInfo(node.Syntax.Type, TestContext.Current.CancellationToken).Type!;

        var matches = CodeType.Named("Missing").Matches(type);

        Assert.Equal(TypeKind.Error, type.TypeKind);
        Assert.False(matches);
    }

    [Fact]
    public void Reference_search_matches_alias_calls_and_excludes_same_named_parameters()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "using Alias = Target; class Target { public static void Read() { } } class Caller { void M(int Read) { Alias.Read(); Target.Read(); _ = Read; } }")]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var target = solution.Methods.Single(method => method.Name == "Read").Symbol!;

        var references = SymbolQueries.ReferencesTo(target).In(solution);

        Assert.Equal(["Read", "Read"], references.Select(reference => reference.Syntax.ToString()));
    }

    [Fact]
    public void Custom_roots_report_each_attribute_with_exact_source_membership()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "[System.Obsolete] class A { }"), new("B.g.cs", "[System.Obsolete] class B { }", true)]);
        var rules = new RuleSet();
        rules.For(Sources.Attributes).Forbid("ATTR", "Review the attribute.");

        var findings = rules.Evaluate(workspace.Analyze(TestContext.Current.CancellationToken));

        Assert.Equal([new SourceLocation("A.cs", 1, 15, 1, 2)], findings.Select(finding => finding.Location));
        Assert.Equal("A.cs", Assert.Single(findings).Source!.Document.Path);
    }

    [Fact]
    public void Declarations_include_properties_parameters_locals_and_partial_occurrences()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "partial class A { public int Value { get; set; } void M(int count) { var local = count; } }"), new("B.cs", "partial class A { }")]);
        var selected = SymbolQueries.Declarations.Where(item => item.Symbol.Kind is SymbolKind.NamedType or SymbolKind.Parameter or SymbolKind.Local or SymbolKind.Property);

        var declarations = selected.In(workspace.Analyze(TestContext.Current.CancellationToken));

        Assert.Equal(["A", "Value", "count", "local", "A"], declarations.Select(item => item.Symbol.Name));
    }

    [Fact]
    public void Syntax_and_constants_remain_available_in_erroneous_source()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { string M() => Missing(\"value\"); }")], allowErrors: true);

        var node = Sources.Nodes<LiteralExpressionSyntax>().In(workspace.Analyze(TestContext.Current.CancellationToken)).Single();

        Assert.Equal("value", node.Constant.Value);
        Assert.Equal(SpecialType.System_String, node.TypeInfo.Type!.SpecialType);
    }
}
