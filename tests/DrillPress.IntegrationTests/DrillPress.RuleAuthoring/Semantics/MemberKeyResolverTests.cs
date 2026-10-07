using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class MemberKeyResolverTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData("\"Address.Name\"", "input.Address.Name", PathCorrelation.Match)]
    [InlineData("\"Items[0].Name\"", "input.Items[0].Name", PathCorrelation.Match)]
    [InlineData("\"Items[0].Name\"", "input.Items[1].Name", PathCorrelation.Different)]
    [InlineData("\"Items[0].Name\"", "input.Items[index].Name", PathCorrelation.Unknown)]
    [InlineData("\"Values[0].Name\"", "input.Values[0].Name", PathCorrelation.Match)]
    [InlineData("\"Address.Name\"", "other.Address.Name", PathCorrelation.Different)]
    [InlineData("nameof(other.Address.Name)", "input.Address.Name", PathCorrelation.Unknown)]
    [InlineData("\"Address.Missing\"", "input.Address.Name", PathCorrelation.Unknown)]
    [InlineData("\"Items[index].Name\"", "input.Items[0].Name", PathCorrelation.Unknown)]
    public void Configured_paths_bind_each_member_root_and_typed_index(
        string key,
        string access,
        PathCorrelation expected
    )
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "C.cs",
                    $$"""
                    class Child { public string Name => ""; }
                    class Model { public Child Address => new(); public Child[] Items => []; public System.Collections.Generic.List<Child> Values => []; }
                    class C { void Key(string key) {} void M(Model input, Model other, int index) { _ = input; Key({{key}}); _ = {{access}}; } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var nodes = Code.Nodes<ExpressionSyntax>().In(solution).ToArray();
        var root = nodes.First(node => node.Syntax.ToString() == "input");
        var keyNode = nodes.First(node => node.Syntax.ToString() == key);
        var checkedNode = nodes.Last(node => node.Syntax.ToString() == access);
        var resolver = new MemberKeyResolver(MemberKeyGrammar.DottedAndIndexed);

        var resolved = resolver.Resolve(
            new(keyNode.Source, keyNode.Syntax),
            new(root.Source, root.Syntax)
        );
        var correlation =
            resolved?.CompareTo(new CodeExpression(checkedNode.Source, checkedNode.Syntax))
            ?? PathCorrelation.Unknown;

        Assert.Equal(expected, correlation);
    }

    [Fact]
    public void Explicit_name_mapping_preserves_ambiguity_and_visibility()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "C.cs",
                    "class Model { public string Name => \"\"; public string Other => \"\"; private string Hidden => \"\"; } class C { void M(Model input) { _ = input; _ = \"external\"; } }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var root = Code.Nodes<IdentifierNameSyntax>()
            .In(solution)
            .First(node => node.Syntax.Identifier.ValueText == "input");
        var key = Code.Nodes<LiteralExpressionSyntax>()
            .In(solution)
            .Single(node => node.Syntax.Token.ValueText == "external");
        var mapped = new MemberKeyResolver(members: (type, _) => type.GetMembers("Name"));
        var ambiguous = new MemberKeyResolver(
            members: (type, _) => type.GetMembers("Name").Concat(type.GetMembers("Other"))
        );
        var hidden = new MemberKeyResolver(members: (type, _) => type.GetMembers("Hidden"));

        var result = new[] { mapped, ambiguous, hidden }
            .Select(resolver =>
                resolver
                    .Resolve(new(key.Source, key.Syntax), new(root.Source, root.Syntax))
                    ?.Steps.Last()
                    .Member?.Name
            )
            .ToArray();

        Assert.Equal(["Name", null, null], result);
    }
}
