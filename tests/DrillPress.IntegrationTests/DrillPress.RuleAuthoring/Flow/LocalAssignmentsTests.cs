using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Flow;

public sealed class LocalAssignmentsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Top_level_writes_in_later_statements_prevent_initializer_traversal()
    {
        var workspace = fixture.Workspace();
        var captured = workspace.AddProject(
            "Program",
            [
                new(
                    "Program.cs",
                    """
                    int value = 1;
                    value = 2;
                    Use(value);
                    static void Use(int value) {}
                    """
                ),
            ],
            allowErrors: true
        );
        var compilation = captured.Compilation.WithOptions(
            captured.Compilation.Options.WithOutputKind(
                Microsoft.CodeAnalysis.OutputKind.ConsoleApplication
            )
        );
        var project = new AnalysisProject(
            captured.Snapshot with
            {
                OutputKind = (int)Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
            },
            compilation,
            TestContext.Current.CancellationToken
        );
        var solution = new AnalysisSolution([project], TestContext.Current.CancellationToken);
        var root = Code
            .Calls.ToMethodsNamed("Use")
            .ArgumentsFor("value")
            .SourceValues()
            .In(solution)
            .Single();

        var fact = root.Facts.IsAssignedOnlyByInitializer;
        var result = root.TraverseInputs(new ExpressionTraversal().ThroughSingleAssignmentLocals());

        Assert.False(fact);
        Assert.Equal(ExpressionTraversalStatus.Unavailable, result.Status);
        Assert.Equal(["value"], result.Values.Select(value => value.Syntax.ToString()));
        Assert.Equal(
            [ExpressionTraversalReason.LocalNotSingleAssignment],
            result.Boundaries.Select(boundary => boundary.Reason)
        );
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("value = 2;", false)]
    [InlineData("value += 2;", false)]
    [InlineData("value++;", false)]
    [InlineData("--value;", false)]
    [InlineData("Ref(ref value);", false)]
    [InlineData("Out(out value);", false)]
    [InlineData("In(in value);", false)]
    [InlineData("In(value);", false)]
    [InlineData("ref int alias = ref value;", false)]
    [InlineData("int other = 0; (value, other) = (2, 3);", false)]
    [InlineData("Action write = () => value = 2;", false)]
    [InlineData("void Write() { value = 2; }", false)]
    [InlineData("{ int value2 = 0; value2 = 2; }", true)]
    [InlineData("this.value = 2;", true)]
    public void Only_initializer_assignment_allows_traversal(string additional, bool expected)
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "using System; class A { int value; static void Use(int value) {} static void Ref(ref int value) {} static void Out(out int value) => value = 2; static void In(in int value) {} void M() { int value = 1; "
                        + additional
                        + " Use(value); } }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var root = Code
            .Calls.ToMethodsNamed("Use")
            .ArgumentsFor("value")
            .SourceValues()
            .In(solution)
            .Single();

        var fact = root.Facts.IsAssignedOnlyByInitializer;
        var result = root.TraverseInputs(new ExpressionTraversal().ThroughSingleAssignmentLocals());

        Assert.Equal(expected, fact);
        Assert.Equal(Convert.ToInt32(expected) + 1, result.Values.Count);
        Assert.Equal(Convert.ToInt32(!expected), result.Boundaries.Count);
        Assert.All(
            result.Boundaries,
            boundary =>
                Assert.Equal(ExpressionTraversalReason.LocalNotSingleAssignment, boundary.Reason)
        );
        Assert.All(result.Values, value => Assert.Same(root.Source, value.Source));
    }

    [Fact]
    public void Local_chains_preserve_source_locations_configuration_and_limits()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { static int Wrap(int input) => input; static void Use(int value) {} void M() { int first = 1; int second = first; Use(Wrap(second)); } }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var root = Code
            .Calls.ToMethodsNamed("Use")
            .ArgumentsFor("value")
            .SourceValues()
            .In(solution)
            .Single();
        var wrap = CodeType.Named("A").Member("Wrap");
        var callsOnly = new ExpressionTraversal().ThroughArgumentOf(wrap, "input");
        var combined = new ExpressionTraversal()
            .ThroughSingleAssignmentLocals()
            .ThroughArgumentOf(wrap, "input");

        var defaultResult = root.TraverseInputs(callsOnly);
        var complete = root.TraverseInputs(combined);
        var depth = root.TraverseInputs(combined, maxDepth: 1);
        var count = root.TraverseInputs(combined, maxExpressions: 2);

        Assert.Equal(
            ["Wrap(second)", "second"],
            defaultResult.Values.Select(value => value.Syntax.ToString())
        );
        Assert.Equal(
            ["Wrap(second)", "second", "first", "1"],
            complete.Values.Select(value => value.Syntax.ToString())
        );
        Assert.Equal(ExpressionTraversalStatus.Complete, complete.Status);
        Assert.Empty(complete.Boundaries);
        Assert.Equal(ExpressionTraversalStatus.LimitExceeded, depth.Status);
        Assert.Equal(
            [ExpressionTraversalReason.DepthLimit],
            depth.Boundaries.Select(boundary => boundary.Reason)
        );
        Assert.Equal(ExpressionTraversalStatus.LimitExceeded, count.Status);
        Assert.Equal(
            [ExpressionTraversalReason.ExpressionLimit],
            count.Boundaries.Select(boundary => boundary.Reason)
        );
        Assert.Equal(
            complete
                .Values[0]
                .Source.Document.Text.IndexOf("Wrap(second)", StringComparison.Ordinal),
            complete.Values[0].Location.Start
        );
        Assert.Equal(
            complete.Values[0].Source.Document.Text.IndexOf("= 1", StringComparison.Ordinal) + 2,
            complete.Values[^1].Location.Start
        );
    }

    [Fact]
    public void Invalid_cycles_stop_with_typed_boundaries()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { static void Use(int value) {} void M() { int first = second; int second = first; Use(second); } }"
                ),
            ],
            allowErrors: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var root = Code
            .Calls.ToMethodsNamed("Use")
            .ArgumentsFor("value")
            .SourceValues()
            .In(solution)
            .Single();

        var result = root.TraverseInputs(new ExpressionTraversal().ThroughSingleAssignmentLocals());

        Assert.Equal(ExpressionTraversalStatus.Unavailable, result.Status);
        Assert.Equal(
            [ExpressionTraversalReason.LocalNotSingleAssignment],
            result.Boundaries.Select(boundary => boundary.Reason)
        );
        Assert.Equal(["second"], result.Values.Select(value => value.Syntax.ToString()));
    }

    [Fact]
    public void Traversal_observes_context_cancellation()
    {
        var workspace = fixture.Workspace();
        var project = workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { static void Use(int value) {} void M() { int value = 1; Use(value); } }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var selected = Code
            .Calls.ToMethodsNamed("Use")
            .ArgumentsFor("value")
            .SourceValues()
            .In(solution)
            .Single();
        var canceled = new AnalysisProject(
            project.Snapshot,
            project.Compilation,
            new CancellationToken(true)
        );
        var expression = new CodeExpression(canceled.Sources.Single(), selected.Syntax);

        Assert.Throws<OperationCanceledException>(() =>
            expression.TraverseInputs(new ExpressionTraversal().ThroughSingleAssignmentLocals())
        );
    }

    [Fact]
    public void Missing_initializers_and_ref_return_aliases_remain_boundaries()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    "class A { static void Use(int value) {} void M() { int value; value = 1; Use(value); } void N() { int value = 1; ref int alias = ref value; Use(value); } }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var roots = Code
            .Calls.ToMethodsNamed("Use")
            .ArgumentsFor("value")
            .SourceValues()
            .In(solution);

        var results = roots
            .Select(root =>
                root.TraverseInputs(new ExpressionTraversal().ThroughSingleAssignmentLocals())
            )
            .ToArray();

        Assert.Equal(
            [ExpressionTraversalStatus.Unavailable, ExpressionTraversalStatus.Unavailable],
            results.Select(result => result.Status)
        );
        Assert.Equal(
            [
                ExpressionTraversalReason.LocalNotSingleAssignment,
                ExpressionTraversalReason.LocalNotSingleAssignment,
            ],
            results.SelectMany(result => result.Boundaries).Select(boundary => boundary.Reason)
        );
    }
}
