using DrillPress.Testing;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Analysis;

public sealed class SourceErrorsTests
{
    [Theory]
    [InlineData("Use(1); Use(2);", new[] { true, true })]
    [InlineData("Use(1 / 0); Use(2);", new[] { false, true })]
    [InlineData("int unused = 1 / 0; Use(2);", new[] { true })]
    [InlineData("void Nested() { Use(1 / 0); } Use(2);", new[] { false, true })]
    public void Resolved_calls_keep_exact_error_spans(string body, bool[] expected)
    {
        var workspace = new RuleTestWorkspace();
        workspace.AddProject(
            "Sample",
            [new("Sample.cs", "class C { void Use(int value) {} void Run() { " + body + " } }")],
            allowErrors: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var resolved = Code.Calls.In(solution).Select(call => call.IsResolved).ToArray();

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Errors_in_one_document_do_not_reject_calls_in_another()
    {
        var workspace = new RuleTestWorkspace();
        workspace.AddProject(
            "Sample",
            [
                new("Clean.cs", "partial class C { int Clean() => Use(2); }"),
                new(
                    "Broken.cs",
                    "partial class C { int Use(int value) => value; int Broken() => Use(1 / 0); }"
                ),
            ],
            allowErrors: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var calls = Code.Calls.ToMethodsNamed("Use").In(solution);

        Assert.Equal(["Clean.cs"], calls.Select(call => call.Source.Document.Path));
    }
}
