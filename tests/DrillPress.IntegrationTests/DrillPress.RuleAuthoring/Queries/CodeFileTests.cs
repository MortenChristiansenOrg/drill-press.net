using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class CodeFileTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Files_know_their_project_relative_path_and_top_level_statements()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "App",
            [
                new("Program.cs", "System.Console.WriteLine(1);"),
                new("Orders/Order.cs", "namespace App.Orders; class Order { }"),
            ],
            outputKind: OutputKind.ConsoleApplication
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var files = Code
            .Files.In(solution)
            .Select(file => $"{file.RelativePath}:{file.HasTopLevelStatements}")
            .Order()
            .ToArray();

        Assert.Equal(["Orders/Order.cs:False", "Program.cs:True"], files);
    }
}
