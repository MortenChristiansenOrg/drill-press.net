using System.IO.Abstractions.TestingHelpers;
using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DrillPress.UnitTests.Engine;

public sealed class SnapshotTreeOptionsTests
{
    [Theory]
    [InlineData(false, GeneratedKind.NotGenerated)]
    [InlineData(true, GeneratedKind.MarkedGenerated)]
    public void Captured_trees_retain_generated_status_and_diagnostic_overrides(
        bool generated,
        GeneratedKind expected
    )
    {
        var compilation = Reconstruct(generated);
        var tree = compilation.SyntaxTrees.Single();
        var options = compilation.Options.SyntaxTreeOptionsProvider!;
        var cancellationToken = TestContext.Current.CancellationToken;

        var kind = options.IsGenerated(tree, cancellationToken);
        var found = options.TryGetDiagnosticValue(
            tree,
            "CS0168",
            cancellationToken,
            out var severity
        );
        var missing = options.TryGetDiagnosticValue(
            tree,
            "CS0219",
            cancellationToken,
            out var fallback
        );

        Assert.Equal(expected, kind);
        Assert.True(found);
        Assert.Equal(ReportDiagnostic.Suppress, severity);
        Assert.False(missing);
        Assert.Equal(ReportDiagnostic.Default, fallback);
    }

    [Fact]
    public void Uncaptured_trees_have_unknown_generated_status_and_no_diagnostic_overrides()
    {
        var compilation = Reconstruct(true);
        var captured = compilation.SyntaxTrees.Single();
        var cancellationToken = TestContext.Current.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(
            captured.GetText(cancellationToken),
            path: captured.FilePath,
            cancellationToken: cancellationToken
        );
        var options = compilation.Options.SyntaxTreeOptionsProvider!;

        var kind = options.IsGenerated(tree, cancellationToken);
        var found = options.TryGetDiagnosticValue(
            tree,
            "CS0168",
            cancellationToken,
            out var severity
        );

        Assert.Equal(GeneratedKind.Unknown, kind);
        Assert.False(found);
        Assert.Equal(ReportDiagnostic.Default, severity);
    }

    private static CSharpCompilation Reconstruct(bool generated)
    {
        var project = TestSnapshots.CreateProject("Source.cs", "class Source { }", generated);
        project = project with
        {
            Documents =
            [
                project.Documents[0] with
                {
                    Options = new SourceOptionsSnapshot
                    {
                        LanguageVersion = (int)LanguageVersion.CSharp14,
                        DiagnosticOptions = new() { ["CS0168"] = (int)ReportDiagnostic.Suppress },
                    },
                },
            ],
        };
        return new AnalysisEngine(new MockFileSystem())
            .Reconstruct(
                CompilationSnapshot.Create(project),
                TestContext.Current.CancellationToken
            )[0]
            .Compilation;
    }
}
