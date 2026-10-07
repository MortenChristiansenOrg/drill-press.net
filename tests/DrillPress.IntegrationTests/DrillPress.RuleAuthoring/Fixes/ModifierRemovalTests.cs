using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ModifierRemovalTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    private const DeclarationBehavior Declaration =
        DeclarationBehavior.Accessibility
        | DeclarationBehavior.Identity
        | DeclarationBehavior.Contract;

    [Theory]
    [InlineData("internal class A {}", "class A {}", true)]
    [InlineData("internal /* keep */ class A {}", "/* keep */ class A {}", true)]
    [InlineData("internal // keep\nclass A {}", "// keep\nclass A {}", true)]
    [InlineData("class A { internal void M() {} }", "class A { internal void M() {} }", false)]
    public async Task Explicit_accessibility_policy_keeps_comments_and_withholds_changed_contracts(
        string source,
        string expected,
        bool hasFix
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", source)]);
        var rules = new RuleCatalog();
        rules
            .Rule("MODIFIER", "Omit this token.")
            .For(Code.Methods.WithExplicitModifier(Modifier.Internal))
            .Forbid(fix: method =>
                Fix.For(method)
                    .RemoveModifier(Modifier.Internal)
                    .MustPreserve(Declaration)
                    .SafeWhen(_ => true)
            )
            .For(Code.TypeDeclarations.WithExplicitModifier(Modifier.Internal))
            .Forbid(fix: type =>
                Fix.For(type)
                    .RemoveModifier(Modifier.Internal)
                    .MustPreserve(Declaration)
                    .SafeWhen(_ => true)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([hasFix], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(expected, result.FixedText("A.cs"));
    }

    [Theory]
    [InlineData("class A { private int field; }", Modifier.Private, "class A { int field; }")]
    [InlineData("class A { private void M() {} }", Modifier.Private, "class A { void M() {} }")]
    [InlineData("interface I { public void M(); }", Modifier.Public, "interface I { void M(); }")]
    [InlineData(
        "class A { protected internal void M() {} }",
        Modifier.Protected,
        "class A { protected internal void M() {} }"
    )]
    [InlineData(
        "class A { static void M() {} }",
        Modifier.Static,
        "class A { static void M() {} }"
    )]
    public async Task Library_proof_removes_only_accessibility_tokens_that_keep_the_declared_accessibility(
        string source,
        Modifier modifier,
        string expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", source)]);
        var rules = new RuleCatalog();
        rules
            .Rule("MODIFIER", "Omit this token.")
            .For(Code.Methods.WithExplicitModifier(modifier))
            .Forbid(fix: method => Fix.For(method).RemoveModifier(modifier).Propose())
            .For(Code.Fields.WithExplicitModifier(modifier))
            .Forbid(fix: field => Fix.For(field).RemoveModifier(modifier).Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Every_field_declarator_participates_in_the_proof()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { private int first, second; }")]);
        var names = Array.Empty<string>();
        var rules = new RuleCatalog();
        rules
            .Rule("MODIFIER", "Omit this token.")
            .For(Code.Fields.Named("first"))
            .Forbid(fix: field =>
                Fix.For(field)
                    .RemoveModifier(Modifier.Private)
                    .MustPreserve(DeclarationBehavior.Identity | DeclarationBehavior.Accessibility)
                    .SafeWhen(change =>
                    {
                        names = change
                            .Symbols.Select(pair => pair.Before.Name + ":" + pair.After.Name)
                            .ToArray();
                        return true;
                    })
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(["first:first", "second:second"], names);
        Assert.Equal("class A { int first, second; }", result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Ordinary_partial_edits_keep_generated_parts_and_complete_symbol_semantics()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new("A.cs", "internal partial class A {}"),
                new("A.g.cs", "internal partial class A {}", true),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("MODIFIER", "Omit this token.")
            .For(Code.Types)
            .Forbid(fix: declaration =>
                Fix.For(declaration)
                    .RemoveModifier(Modifier.Internal)
                    .MustPreserve(Declaration)
                    .SafeWhen(_ => true)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal("partial class A {}", result.FixedText("A.cs"));
        Assert.Equal("internal partial class A {}", result.FixedText("A.g.cs"));
    }

    [Fact]
    public async Task Identity_and_accessibility_do_not_authorize_static_removal()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { static void M() {} }")]);
        var rules = new RuleCatalog();
        rules
            .Rule("MODIFIER", "Omit this token.")
            .For(Code.Methods)
            .Forbid(fix: method =>
                Fix.For(method)
                    .RemoveModifier(Modifier.Static)
                    .MustPreserve(Declaration)
                    .SafeWhen(_ => true)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(result.Findings).HasFix);
        Assert.Equal("class A { static void M() {} }", result.FixedText("A.cs"));
    }
}
