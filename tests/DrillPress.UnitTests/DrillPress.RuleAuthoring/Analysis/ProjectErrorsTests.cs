using DrillPress.Testing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Analysis;

public sealed class ProjectErrorsTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("1 / 0", false)]
    public void Broad_operation_discovery_keeps_each_calls_exact_error_status(
        string value,
        bool firstResolved
    )
    {
        var sources = new List<TestSource>
        {
            new(
                "Part0.cs",
                "partial class C { void Use(int value) {} void Run0() { Use(" + value + "); } }"
            ),
        };
        sources.AddRange(
            Enumerable
                .Range(1, 7)
                .Select(index => new TestSource(
                    $"Part{index}.cs",
                    $"partial class C {{ void Run{index}() {{ Use({index}); }} }}"
                ))
        );
        var workspace = new RuleTestWorkspace();
        workspace.AddProject("Sample", sources, allowErrors: true);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var resolved = Code.Calls.In(solution).Select(call => call.IsResolved).ToArray();

        Assert.Equal([firstResolved, true, true, true, true, true, true, true], resolved);
    }

    [Theory]
    [InlineData("[System.Obsolete] void Use(int value) {}", "1")]
    [InlineData("void Use(string value) {}", "null")]
    public void Broad_operation_discovery_preserves_promoted_warnings(
        string declaration,
        string value
    )
    {
        var workspace = new RuleTestWorkspace();
        var sources = Enumerable
            .Range(0, 8)
            .Select(index => new TestSource(
                $"Part{index}.cs",
                $"partial class C {{ void Run{index}() {{ Use({value}); }} }}"
            ))
            .Append(new TestSource("Declaration.cs", $"partial class C {{ {declaration} }}"))
            .ToArray();
        var captured = workspace.AddProject("Sample", sources, allowErrors: true);
        var compilation = captured.Compilation.WithOptions(
            captured.Compilation.Options.WithGeneralDiagnosticOption(ReportDiagnostic.Error)
        );
        var snapshot = captured.Snapshot with
        {
            CompilerOptions = captured.Snapshot.CompilerOptions with
            {
                GeneralDiagnosticOption = (int)ReportDiagnostic.Error,
            },
        };
        var project = new AnalysisProject(
            snapshot,
            compilation,
            TestContext.Current.CancellationToken
        );
        var solution = new AnalysisSolution([project], TestContext.Current.CancellationToken);

        var resolved = Code.Calls.In(solution).Select(call => call.IsResolved).ToArray();

        Assert.Equal([false, false, false, false, false, false, false, false], resolved);
    }
}
