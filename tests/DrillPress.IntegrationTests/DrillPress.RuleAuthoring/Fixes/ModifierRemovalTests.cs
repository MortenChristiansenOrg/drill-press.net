using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ModifierRemovalTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
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
        var rules = new RuleSet();
        rules
            .For(
                Sources
                    .Nodes<MemberDeclarationSyntax>()
                    .Where(node => node.Syntax.GetFirstToken().IsKind(SyntaxKind.InternalKeyword))
            )
            .Forbid(
                "MODIFIER",
                "Omit this token.",
                fix: node =>
                    Fix.For(node)
                        .RemoveModifier(SyntaxKind.InternalKeyword)
                        .Require(DeclarationChecks.SameDeclaredAccessibility)
                        .Require(DeclarationChecks.SameIdentity)
                        .Require(DeclarationChecks.SameContract)
                        .Propose(_ => ProofResult.Proven)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([hasFix], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(expected, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Every_field_declarator_participates_in_the_proof()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { private int first, second; }")]);
        var names = Array.Empty<string>();
        var rules = new RuleSet();
        rules
            .For(Sources.Nodes<FieldDeclarationSyntax>())
            .Forbid(
                "MODIFIER",
                "Omit this token.",
                fix: node =>
                    Fix.For(node)
                        .RemoveModifier(SyntaxKind.PrivateKeyword)
                        .Require(DeclarationChecks.SameIdentity)
                        .Require(DeclarationChecks.SameDeclaredAccessibility)
                        .Propose(change =>
                        {
                            names = change
                                .Symbols.Select(pair => pair.Before.Name + ":" + pair.After.Name)
                                .ToArray();
                            return ProofResult.Proven;
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
        var rules = new RuleSet();
        rules
            .For(Code.Types)
            .Forbid(
                "MODIFIER",
                "Omit this token.",
                fix: declaration =>
                    Fix.For(declaration)
                        .RemoveModifier(SyntaxKind.InternalKeyword)
                        .Require(DeclarationChecks.SameIdentity)
                        .Require(DeclarationChecks.SameDeclaredAccessibility)
                        .Require(DeclarationChecks.SameContract)
                        .Propose(_ => ProofResult.Proven)
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
        var rules = new RuleSet();
        rules
            .For(Code.Methods)
            .Forbid(
                "MODIFIER",
                "Omit this token.",
                fix: method =>
                    Fix.For(method)
                        .RemoveModifier(SyntaxKind.StaticKeyword)
                        .Require(DeclarationChecks.SameIdentity)
                        .Require(DeclarationChecks.SameDeclaredAccessibility)
                        .Require(DeclarationChecks.SameContract)
                        .Propose(_ => ProofResult.Proven)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(result.Findings).HasFix);
        Assert.Equal("class A { static void M() {} }", result.FixedText("A.cs"));
    }
}
