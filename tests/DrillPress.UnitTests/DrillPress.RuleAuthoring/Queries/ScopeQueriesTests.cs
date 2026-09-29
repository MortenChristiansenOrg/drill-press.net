using System.IO.Abstractions.TestingHelpers;
using DrillPress.Manifest;
using DrillPress.Queries;
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

        var selected = Sources.Files.InFolder("Endpoints").InFilesNamed("*Tests.cs").In(solution);

        Assert.Same(parent, Assert.Single(selected).Source.Project);
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
            Sources.Files.InFolder("Endpoints").In(solution).Count,
            Sources.Files.InFolder("Endpoints", sourceRoot: "/repo/Endpoints").In(solution).Count,
        };

        Assert.Equal([1, 0], counts);
    }
}
