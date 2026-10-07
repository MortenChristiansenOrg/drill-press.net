using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class NameQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Unnamed_symbols_have_no_name_words_and_never_match_a_word()
    {
        var workspace = fixture.Workspace();
        var project = workspace.AddProject("Library", [new("A.cs", "class A {}")]);
        var symbol = project.Compilation.GlobalNamespace;

        var words = symbol.NameWords();
        var contains = symbol.NameContainsWord("A");

        Assert.Empty(words);
        Assert.False(contains);
    }

    [Fact]
    public void Missing_written_identifiers_have_no_words_or_matches()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Library", [new("A.cs", "class {}")], allowErrors: true);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var declaration = Code.TypeDeclarations.In(solution).Single();
        var words = declaration.NameWords;
        var contains = declaration.NameContainsWord("A");
        var selected = Code.TypeDeclarations.WithNameContainingAnyWord(["A"]).In(solution);

        Assert.Empty(words);
        Assert.False(contains);
        Assert.Empty(selected);
    }

    [Fact]
    public void Word_helpers_cover_named_declarations_parameters_and_locals()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class HTTPClient2 { void Get_ReturnsNull(string nullableValue) { int nullCount = 1; } void Get_ReturnsNullable() {} }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var methods = Code
            .Methods.WithNameContainingAnyWord(["null"], StringComparison.OrdinalIgnoreCase)
            .In(solution);
        var method = methods.Single();
        var local = Code.LocalVariables.In(solution).Single();
        var localSymbol = (Microsoft.CodeAnalysis.ILocalSymbol)
            local.Source.Model.GetDeclaredSymbol(
                local.Variables.Single().Syntax,
                TestContext.Current.CancellationToken
            )!;

        Assert.Equal(["Get", "Returns", "Null"], method.NameWords);
        Assert.Equal(["HTTP", "Client", "2"], Code.Types.In(solution).Single().NameWords);
        Assert.Equal(
            ["HTTP", "Client", "2"],
            Code.TypeDeclarations.In(solution).Single().NameWords
        );
        Assert.True(method.NameContainsWord("null", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["nullable", "Value"], method.Symbol!.Parameters.Single().NameWords());
        Assert.True(localSymbol.NameContainsWord("null"));
        Assert.Equal(
            ["HTTPClient2"],
            Code.Types.WithNameContainingAnyWord(["HTTP"]).In(solution).Select(type => type.Name)
        );
    }

    [Theory]
    [InlineData("Acme.**", new[] { "Root", "Child", "Leaf" })]
    [InlineData("Acme", new[] { "Root" })]
    [InlineData("Acme.*", new[] { "Child" })]
    [InlineData("**", new[] { "Global", "Root", "Child", "Leaf", "Other" })]
    [InlineData("**.Services", new[] { "Child" })]
    public void Namespace_globs_match_complete_segments_and_trailing_recursive_roots(
        string pattern,
        string[] expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class Global {} namespace Acme { class Root {} } namespace Acme.Services { class Child {} } namespace Acme.Services.Data { class Leaf {} } namespace AcmeOther { class Other {} }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var selected = Code
            .Types.In(solution)
            .Where(type => type.Symbol.IsInNamespace(pattern))
            .Select(type => type.Name)
            .ToArray();

        Assert.Equal(expected, selected);
    }
}
