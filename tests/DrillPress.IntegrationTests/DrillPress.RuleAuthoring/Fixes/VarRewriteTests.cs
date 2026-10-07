using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class VarRewriteTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData("int value = 1;", true, "var value = 1;")]
    [InlineData("long value = 1;", false, "long value = 1;")]
    [InlineData("long value = 1L;", true, "var value = 1L;")]
    [InlineData("int? value = 1;", false, "int? value = 1;")]
    [InlineData("string? value = \"x\";", true, "var value = \"x\";")]
    [InlineData("string value = \"x\";", false, "string value = \"x\";")]
    [InlineData("string value = null!;", false, "string value = null!;")]
    [InlineData("object value = \"x\";", false, "object value = \"x\";")]
    [InlineData("Foo value = new();", false, "Foo value = new();")]
    [InlineData("Foo value = new Foo();", false, "Foo value = new Foo();")]
    [InlineData("Foo? value = new Foo();", true, "var value = new Foo();")]
    [InlineData("List<int> value = [];", false, "List<int> value = [];")]
    [InlineData(
        "IReadOnlyList<int> value = new List<int>();",
        false,
        "IReadOnlyList<int> value = new List<int>();"
    )]
    [InlineData("Func<int> value = () => 1;", false, "Func<int> value = () => 1;")]
    [InlineData("Func<int> value = Value;", false, "Func<int> value = Value;")]
    [InlineData("Func<int>? value = Value;", false, "Func<int>? value = Value;")]
    [InlineData("int value = Value();", true, "var value = Value();")]
    [InlineData(
        "foreach (ref int item in new int[] { 1 }.AsSpan()) {}",
        false,
        "foreach (ref int item in new int[] { 1 }.AsSpan()) {}"
    )]
    [InlineData("int value = default;", false, "int value = default;")]
    [InlineData("int value = default(int);", true, "var value = default(int);")]
    [InlineData(
        "Foo value = true ? new() : new Foo();",
        false,
        "Foo value = true ? new() : new Foo();"
    )]
    [InlineData(
        "Foo value = 1 switch { _ => new() };",
        false,
        "Foo value = 1 switch { _ => new() };"
    )]
    [InlineData("Foo value = (new());", false, "Foo value = (new());")]
    [InlineData("int first = 1, second = 2;", false, "int first = 1, second = 2;")]
    [InlineData(
        "(int First, int Second) value = (First: 1, Second: 2);",
        true,
        "var value = (First: 1, Second: 2);"
    )]
    [InlineData(
        "(int First, int Second) value = (1, 2);",
        false,
        "(int First, int Second) value = (1, 2);"
    )]
    [InlineData(
        "(int First, string? Second) value = (First: 1, Second: \"x\");",
        false,
        "(int First, string? Second) value = (First: 1, Second: \"x\");"
    )]
    [InlineData(
        "foreach (int item in new[] { 1 }) {}",
        true,
        "foreach (var item in new[] { 1 }) {}"
    )]
    [InlineData(
        "foreach (long item in new[] { 1 }) {}",
        false,
        "foreach (long item in new[] { 1 }) {}"
    )]
    [InlineData(
        "foreach (Derived item in new Base[] { new Derived() }) {}",
        false,
        "foreach (Derived item in new Base[] { new Derived() }) {}"
    )]
    [InlineData("int.TryParse(\"1\", out int value);", true, "int.TryParse(\"1\", out var value);")]
    [InlineData("Pick(out int value);", false, "Pick(out int value);")]
    public async Task Inference_selection_and_fix_agree(
        string statement,
        bool expected,
        string expectedStatement
    )
    {
        var workspace = fixture.Workspace();
        var source =
            "using System; using System.Collections.Generic; class Foo {} class Base {} class Derived : Base {} class A { static int Value() => 1; static void Pick(out int value) => value = 1; static void Pick(out string value) => value = \"x\"; void M() { "
            + statement
            + " } }";
        workspace.AddProject("Library", [new("A.cs", source)]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var query = Code.LocalVariables.Concat(Code.ForEachLoops).Concat(Code.OutVariables);
        var declaration = query.In(solution).Single();
        var rules = new RuleSet();
        rules
            .Rule("VAR", "Use var.")
            .For(query.WhereVarPreservesType())
            .Forbid(fix: item => Fix.For(item).UseVar().Propose());

        var canUseVar = declaration.CanUseVar;
        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(expected, canUseVar);
        Assert.Equal(Convert.ToInt32(expected), result.Findings.Count);
        Assert.All(result.Findings, finding => Assert.True(finding.HasFix));
        Assert.Equal(
            source.Replace(statement, expectedStatement, StringComparison.Ordinal),
            result.FixedText("A.cs")
        );
    }

    [Theory]
    [InlineData("using var = System.Int32; class A { void M() { int value = 1; } }", false)]
    [InlineData("class var {} class A { void M() { int value = 1; } }", false)]
    [InlineData("class A { void M() { Missing value = new Missing(); } }", false)]
    [InlineData("class A { void M() { int value; } }", false)]
    [InlineData("class A { void M() { var value = 1; } }", false)]
    public void Unavailable_and_shadowed_var_declarations_are_excluded(string source, bool expected)
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", source)], allowErrors: true);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var declaration = Code.LocalVariables.In(solution).Single();
        var proposal = Fix.For(declaration).UseVar().Propose();

        Assert.Equal(expected, declaration.CanUseVar);
        Assert.Null(proposal);
    }

    [Fact]
    public async Task Linked_context_with_a_var_alias_withholds_an_otherwise_eligible_fix()
    {
        var workspace = fixture.Workspace();
        const string source = """
            #if ALIAS
            using var = System.Int32;
            #endif
            class A { void M() { int value = 1; } }
            """;
        workspace.AddProject("Primary", [new("A.cs", source)]);
        workspace.AddProject("Aliased", [new("A.cs", source)], symbols: ["ALIAS"]);
        var rules = new RuleSet();
        rules
            .Rule("VAR", "Use var.")
            .For(
                Code.LocalVariables.Where(item => item.Source.Project.Name == "Primary")
                    .WhereVarPreservesType()
            )
            .Forbid(fix: item => Fix.For(item).UseVar().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([false], result.Findings.Select(finding => finding.HasFix));
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Type_replacement_preserves_trivia_and_validates_linked_contexts()
    {
        var workspace = fixture.Workspace();
        const string source = "class A { void M() { /* before */ int /* after */ value = 1; } }";
        workspace.AddProject("Library", [new("A.cs", source)]);
        workspace.AddProject("Other", [new("A.cs", source)], framework: "net9.0");
        var rules = new RuleSet();
        rules
            .Rule("VAR", "Use var.")
            .For(Code.LocalVariables.WhereVarPreservesType())
            .Forbid(fix: item => Fix.For(item).UseVar().Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            [new TestFinding("VAR", "A.cs", 1, 35, "int /* after */ value = 1", true)],
            result.Findings
        );
        Assert.Equal(
            "class A { void M() { /* before */ var /* after */ value = 1; } }",
            result.FixedText("A.cs")
        );
    }
}
