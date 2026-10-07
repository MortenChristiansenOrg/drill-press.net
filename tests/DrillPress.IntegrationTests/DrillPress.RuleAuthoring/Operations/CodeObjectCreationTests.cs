using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class CodeObjectCreationTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Creations_cover_explicit_and_target_typed_forms_with_mapped_arguments()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    """
                    using System.Collections.Generic;
                    class Client { public Client(string name, int retries = 3) { } }
                    class A
                    {
                        Client first = new Client("first");
                        Client second = new(retries: 1, name: "second");
                        List<int> numbers = new List<int> { 1 };
                        T Make<T>() where T : new() => new T();
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var creations = Code
            .ObjectCreations.In(solution)
            .Select(creation =>
                $"{creation.Syntax}|{creation.Type?.Name}|{creation.IsTargetTyped}|{creation.IsResolved}|{creation.Argument("name")?.Syntax}|{string.Join(",", creation.Arguments.Select(argument => argument.Syntax))}"
            )
            .ToArray();
        var clients = Code.ObjectCreations.Of(CodeType.Named("Client")).In(solution).Count;
        var lists = Code
            .ObjectCreations.Of(CodeType.Named("System.Collections.Generic.List<>"))
            .In(solution)
            .Count;
        var constructor = CodeType
            .Named("Client")
            .Constructor(CodeType.Of<string>(), CodeType.Of<int>());

        Assert.Equal(
            [
                "new Client(\"first\")|Client|False|True|\"first\"|\"first\"",
                "new(retries: 1, name: \"second\")|Client|True|True|\"second\"|1,\"second\"",
                "new List<int> { 1 }|List|False|True||",
                "new T()||False|False||",
            ],
            creations
        );
        Assert.Equal(2, clients);
        Assert.Equal(1, lists);
        Assert.All(
            Code.ObjectCreations.Of(CodeType.Named("Client")).In(solution),
            creation => Assert.True(creation.Calls(constructor))
        );
    }

    [Fact]
    public async Task Rules_can_forbid_creating_a_type_directly()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "using System.Net.Http;\nclass A\n{\n    HttpClient client = new HttpClient();\n    HttpClient other = new();\n}\n"
                ),
            ]
        );
        var rules = new RuleSet();
        rules
            .Rule("HTTP001", "Use IHttpClientFactory instead of creating HttpClient.")
            .For(Code.ObjectCreations.Of<System.Net.Http.HttpClient>())
            .Forbid();

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            HTTP001 Use IHttpClientFactory instead of creating HttpClient.
            A.cs
              4:25
              5:24

            """.Replace("\r\n", "\n"),
            result.Output
        );
    }
}
