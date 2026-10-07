using System.IO.Abstractions.TestingHelpers;
using DrillPress;
using DrillPress.Manifest;
using DrillPress.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Queries;

public sealed class ScopeQueriesTests
{
    [Fact]
    public void Linked_physical_file_is_scoped_relative_to_each_project()
    {
        var files = new MockFileSystem();
        files.Directory.CreateDirectory("/repo");
        files.Directory.SetCurrentDirectory("/repo");
        var document = new DocumentSnapshot("/repo/Endpoints/ItemTests.cs", "class C {}", false);
        var compilation = CSharpCompilation.Create(
            "Test",
            [
                CSharpSyntaxTree.ParseText(
                    document.Text,
                    path: document.Path,
                    cancellationToken: TestContext.Current.CancellationToken
                ),
            ]
        );
        var snapshot = new ProjectSnapshot(
            "Test",
            "Test",
            "/repo/Test.csproj",
            0,
            0,
            0,
            [],
            [document],
            []
        );
        var parent = new AnalysisProject(
            snapshot,
            compilation,
            files,
            TestContext.Current.CancellationToken
        );
        var child = new AnalysisProject(
            snapshot with
            {
                ProjectPath = "/repo/Endpoints/Test.csproj",
            },
            compilation,
            files,
            TestContext.Current.CancellationToken
        );
        var outside = new AnalysisProject(
            snapshot with
            {
                ProjectPath = "/repo/App/Test.csproj",
            },
            compilation,
            files,
            TestContext.Current.CancellationToken
        );
        var solution = new AnalysisSolution(
            [parent, child, outside],
            TestContext.Current.CancellationToken
        );

        var selected = Code.Files.InFolder("Endpoints").InFilesNamed("*Tests.cs").In(solution);
        var membership = Code.Files.In(solution).Select(file => file.IsInFolder("Endpoints"));

        Assert.Same(parent, Assert.Single(selected).Source.Project);
        Assert.Equal([true, false, false], membership);
    }

    [Fact]
    public void Loose_source_root_overrides_captured_invocation_directory()
    {
        var files = new MockFileSystem();
        files.Directory.CreateDirectory("/repo/Endpoints");
        files.Directory.SetCurrentDirectory("/repo");
        var document = new DocumentSnapshot("/repo/Endpoints/Item.cs", "class C {}", false);
        var compilation = CSharpCompilation.Create(
            "Test",
            [
                CSharpSyntaxTree.ParseText(
                    document.Text,
                    path: document.Path,
                    cancellationToken: TestContext.Current.CancellationToken
                ),
            ]
        );
        var snapshot = new ProjectSnapshot(
            "Test",
            "Test",
            document.Path,
            0,
            0,
            0,
            [],
            [document],
            []
        )
        {
            ContextId = "loose",
        };
        var project = new AnalysisProject(
            snapshot,
            compilation,
            files,
            TestContext.Current.CancellationToken
        );
        var solution = new AnalysisSolution([project], TestContext.Current.CancellationToken);
        files.Directory.SetCurrentDirectory("/repo/Endpoints");

        var counts = new[]
        {
            Code.Files.InFolder("Endpoints").In(solution).Count,
            Code.Files.InFolder("Endpoints", sourceRoot: "/repo/Endpoints").In(solution).Count,
        };

        Assert.Equal([1, 0], counts);
    }

    [Fact]
    public void Project_scopes_use_globs_and_evaluated_test_classification()
    {
        var workspace = new RuleTestWorkspace([]);
        workspace.AddProject("Shop.Api", [new("Api.cs", "class Api {}")], allowErrors: true);
        workspace.AddProject(
            "Shop.Api.Tests",
            [new("Tests.cs", "class Tests {}")],
            isTest: true,
            allowErrors: true
        );
        workspace.AddProject("Tools", [new("Tool.cs", "class Tool {}")], allowErrors: true);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var shop = Code.Files.InProject("Shop.*").In(solution).Select(file => file.Name);
        var exact = Code.Files.InProject("Shop.Api").In(solution).Select(file => file.Name);
        var tests = Code.Files.InTestProjects().In(solution).Select(file => file.Name);
        var production = Code.Files.InNonTestProjects().In(solution).Select(file => file.Name);
        var matches = Code.Files.In(solution).Select(file => file.IsInProject("*.Tests")).ToArray();

        Assert.Equal(["Api.cs", "Tests.cs"], shop);
        Assert.Equal(["Api.cs"], exact);
        Assert.Equal(["Tests.cs"], tests);
        Assert.Equal(["Api.cs", "Tool.cs"], production);
        Assert.Equal([false, true, false], matches);
    }
}
