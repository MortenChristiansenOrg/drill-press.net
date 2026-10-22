using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class ModifierAdditionTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData("class A { }", "sealed class A { }")]
    [InlineData("public partial class A { }", "public sealed partial class A { }")]
    [InlineData(
        "[System.Obsolete] internal class A { }",
        "[System.Obsolete] internal sealed class A { }"
    )]
    [InlineData(
        "/// <summary>A.</summary>\npublic class A { }",
        "/// <summary>A.</summary>\npublic sealed class A { }"
    )]
    public async Task Classes_without_derived_types_are_sealed_in_conventional_order(
        string source,
        string expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", source)]);

        var result = await workspace.CheckAsync(
            Rules(Modifier.Sealed),
            TestContext.Current.CancellationToken
        );

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(expected, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Static_replaces_a_written_sealed_modifier()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "sealed class A { public static int Count() => 1; }")]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("STATIC", "Use static.")
            .For(Code.Types.Named("A"))
            .Forbid(fix: type => Fix.For(type).AddModifier(Modifier.Static).Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal("STATIC Use static.\nA.cs\n  +1:14\n", result.Output);
        Assert.Equal(
            "static class A { public static int Count() => 1; }",
            result.FixedText("A.cs")
        );
    }

    [Fact]
    public async Task A_class_derived_from_in_another_project_is_not_sealed()
    {
        var workspace = fixture.Workspace();
        var library = workspace.AddProject("Library", [new("A.cs", "public class A { }")]);
        workspace.AddProject("App", [new("B.cs", "class B : A { }")], dependencies: [library]);
        var rules = new RuleCatalog();
        rules
            .Rule("SEALED", "Seal the class.")
            .For(Code.TypeDeclarations.Named("A"))
            .Forbid(fix: type => Fix.For(type).AddModifier(Modifier.Sealed).Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            SEALED Seal the class.
            A.cs
              1:14
                fix withheld: B derives from the class

            """.ReplaceLineEndings("\n"),
            result.ExplainedOutput
        );
    }

    [Fact]
    public async Task A_class_used_only_through_its_static_members_becomes_static()
    {
        var workspace = fixture.Workspace();
        var library = workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "public class A { public const int Size = 1; public static int Twice(int value) => value * Size; }"
                ),
            ]
        );
        workspace.AddProject(
            "App",
            [
                new(
                    "B.cs",
                    "using static A; class B { int M() => A.Twice(nameof(A).Length) + Twice(Size); }"
                ),
            ],
            dependencies: [library]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("STATIC", "Make the class static.")
            .For(Code.TypeDeclarations.Named("A"))
            .Forbid(fix: type => Fix.For(type).AddModifier(Modifier.Static).Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.StartsWith("public static class A {", result.FixedText("A.cs"));
    }

    [Theory]
    [InlineData(
        "class B { object M() => new A(); }",
        "`A` at 1:29 in B.cs uses the class as a type"
    )]
    [InlineData(
        "class B { System.Type M() => typeof(A); }",
        "`A` at 1:37 in B.cs uses the class as a type"
    )]
    public async Task A_class_used_as_a_type_in_another_project_does_not_become_static(
        string use,
        string reason
    )
    {
        var workspace = fixture.Workspace();
        var library = workspace.AddProject(
            "Library",
            [new("A.cs", "public class A { public static int Value => 1; }")]
        );
        workspace.AddProject("App", [new("B.cs", use)], dependencies: [library]);
        var rules = new RuleCatalog();
        rules
            .Rule("STATIC", "Make the class static.")
            .For(Code.TypeDeclarations.Named("A"))
            .Forbid(fix: type => Fix.For(type).AddModifier(Modifier.Static).Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            $"STATIC Make the class static.\nA.cs\n  1:14\n    fix withheld: {reason}\n",
            result.ExplainedOutput
        );
    }

    [Fact]
    public async Task Instance_members_keep_a_class_from_becoming_static()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class A { int Value => 1; }")]);

        var result = await workspace.CheckAsync(
            Rules(Modifier.Static),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            "MODIFIER Add the modifier.\nA.cs\n  1:7\n    fix withheld: Value is an instance member\n",
            result.ExplainedOutput
        );
    }

    [Fact]
    public async Task Other_modifiers_need_a_rule_proof()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "class A { private int _value; int M() => _value; }")]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("READONLY", "Make the field readonly.")
            .For(Code.Fields)
            .Forbid(fix: field =>
                Fix.For(field)
                    .AddModifier(Modifier.ReadOnly)
                    .MustPreserve(DeclarationBehavior.Identity)
                    .SafeWhen(change => change.AddedModifier == Modifier.ReadOnly)
            );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(result.Findings).HasFix);
        Assert.Equal(
            "class A { private readonly int _value; int M() => _value; }",
            result.FixedText("A.cs")
        );
    }

    private static RuleCatalog Rules(Modifier modifier)
    {
        var rules = new RuleCatalog();
        rules
            .Rule("MODIFIER", "Add the modifier.")
            .For(Code.TypeDeclarations)
            .Forbid(fix: type => Fix.For(type).AddModifier(modifier).Propose());
        return rules;
    }
}
