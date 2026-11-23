using DrillPress.Testing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Fixes;

public sealed class CompilationErrorsTests
{
    private const string Context = """
        public sealed class FixtureSet<T> { }
        public sealed class Context
        {
            public FixtureSet<string> Items { get; set; }
            public int Count => 1;
        }
        """;

    [Theory]
    [InlineData("CS8618", true)]
    [InlineData("CS0168", false)]
    public void Only_warnings_a_project_suppressor_can_hide_leave_fixes_in_an_erroneous_compilation(
        string suppressible,
        bool safe
    )
    {
        var project = WarningsAsErrors(Context, suppressible);
        var proposal = Replace(project, "1", "2");

        var result = proposal.IsSafeIn(project);

        Assert.Equal(safe, result);
    }

    [Fact]
    public void An_edit_adding_a_suppressible_warning_is_withheld()
    {
        var project = WarningsAsErrors(Context, "CS8618");
        var proposal = Replace(
            project,
            "public int Count => 1;",
            "public string Name { get; set; }"
        );

        var result = proposal.IsSafeIn(project);

        Assert.False(result);
    }

    [Fact]
    public void An_edit_moving_a_suppressible_warning_to_another_declaration_is_withheld()
    {
        var project = WarningsAsErrors(
            """
            public sealed class FixtureSet<T> { }
            public sealed class Context
            {
                public FixtureSet<string> Items { get; set; }
            }
            public sealed class Other
            {
                public FixtureSet<string> Items { get; set; } = new();
            }
            """,
            "CS8618"
        );
        var source = project.Sources.Single();
        var text = source.Document.Text;
        var initialized = text.IndexOf(" = new();", StringComparison.Ordinal);
        var uninitialized = text.IndexOf("{ get; set; }", StringComparison.Ordinal);
        var proposal = SourceChanges.Propose(
            [
                SourceChanges.Replace(source, new(uninitialized, 13), "{ get; set; } = new();"),
                SourceChanges.Replace(source, new(initialized, 9), ""),
            ],
            _ => true
        );

        var result = proposal.IsSafeIn(project);

        Assert.False(result);
    }

    [Fact]
    public void An_edit_moving_a_suppressible_warning_to_another_methods_local_is_withheld()
    {
        var project = WarningsAsErrors(
            """
            public sealed class C
            {
                public int A() { int value = 1; return 0; }
                public int B() { int value = 1; return value; }
            }
            """,
            "CS0219"
        );
        var source = project.Sources.Single();
        var text = source.Document.Text;
        var unused = text.IndexOf("return 0;", StringComparison.Ordinal);
        var used = text.IndexOf("return value;", StringComparison.Ordinal);
        var proposal = SourceChanges.Propose(
            [
                SourceChanges.Replace(source, new(unused, 9), "return value;"),
                SourceChanges.Replace(source, new(used, 13), "return 0;"),
            ],
            _ => true
        );

        var result = proposal.IsSafeIn(project);

        Assert.False(result);
    }

    [Fact]
    public void An_error_a_suppressor_names_but_cannot_hide_withholds_fixes()
    {
        var project = WarningsAsErrors(
            Context.Replace(
                "public int Count => 1;",
                "public int Count => 1; int Missing => Absent;"
            ),
            "CS0103"
        );
        var proposal = Replace(project, "1", "2");

        var result = proposal.IsSafeIn(project);

        Assert.False(result);
    }

    private static AnalysisProject WarningsAsErrors(string source, string suppressible)
    {
        var captured = new RuleTestWorkspace().AddProject(
            "Sample",
            [new("Context.cs", source)],
            allowErrors: true
        );
        var compilation = captured.Compilation.WithOptions(
            captured.Compilation.Options.WithGeneralDiagnosticOption(ReportDiagnostic.Error)
        );
        var snapshot = captured.Snapshot with
        {
            CompilerOptions = captured.Snapshot.CompilerOptions with
            {
                GeneralDiagnosticOption = (int)ReportDiagnostic.Error,
            },
            SuppressibleDiagnosticIds = [suppressible],
        };
        return new AnalysisProject(snapshot, compilation, TestContext.Current.CancellationToken);
    }

    private static FixProposal Replace(AnalysisProject project, string original, string replacement)
    {
        var source = project.Sources.Single();
        var start = source.Document.Text.IndexOf(original, StringComparison.Ordinal);
        return SourceChanges.Propose(
            [SourceChanges.Replace(source, new(start, original.Length), replacement)],
            _ => true
        );
    }
}
