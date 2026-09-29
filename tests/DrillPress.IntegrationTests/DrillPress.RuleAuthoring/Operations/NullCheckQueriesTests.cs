using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class NullCheckQueriesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Compound_evaluation_does_not_reuse_an_earlier_statement_entry_state()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Checks",
            [
                new(
                    "C.cs",
                    """
                    class C { void M(string? value) {
                        _ = (value = null) != null || value is null;
                    } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var states = OperationQueries
            .NullChecks.In(solution)
            .Select(check => check.FlowStateBeforeCheck)
            .ToArray();

        Assert.Equal([NullableFlowState.None, NullableFlowState.None], states);
    }

    [Fact]
    public void Linked_nullable_contexts_keep_their_evidence_separate()
    {
        var workspace = fixture.Workspace();
        var source = new TestSource(
            "C.cs",
            """
            #if OBLIVIOUS
            #nullable disable
            #endif
            class C { void M(string value) { _ = value is null; } }
            """
        );
        workspace.AddProject("Enabled", [source]);
        workspace.AddProject("Disabled", [source], symbols: ["OBLIVIOUS"]);
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var states = OperationQueries
            .NullChecks.In(solution)
            .Select(check =>
                $"{check.CheckedValue.DeclaredNullability}:{check.FlowStateBeforeCheck}"
            )
            .ToArray();

        Assert.Equal(["NotAnnotated:NotNull", "None:None"], states);
    }

    [Fact]
    public void Null_shapes_normalize_polarity_without_duplicate_parentheses_or_negations()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Checks",
            [
                new(
                    "C.cs",
                    """
                    class C { void M(string? text, int? number) {
                        _ = text is null; _ = text is not null; _ = null == text; _ = text != null;
                        _ = !((text is null)); _ = !number.HasValue; _ = number.HasValue == false;
                    } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var actual = OperationQueries
            .NullChecks.In(solution)
            .Select(check =>
                $"{check.Condition.Syntax}:{check.CheckedValue.Syntax}:{check.Polarity}:{check.Domain}"
            )
            .ToArray();

        Assert.Equal(
            [
                "text is null:text:IsNull:Reference",
                "text is not null:text:IsNotNull:Reference",
                "null == text:text:IsNull:Reference",
                "text != null:text:IsNotNull:Reference",
                "!((text is null)):text:IsNotNull:Reference",
                "!number.HasValue:number:IsNull:NullableValue",
                "number.HasValue == false:number:IsNull:NullableValue",
            ],
            actual
        );
    }

    [Fact]
    public void Nullable_facts_are_measured_before_the_check_refines_the_operand()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Checks",
            [
                new(
                    "C.cs",
                    """
                    class C { void M(string? maybe, string declared) {
                        if (maybe is null) return;
                        _ = maybe is null;
                        _ = declared is null;
                    } }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var states = OperationQueries
            .NullChecks.In(solution)
            .Select(check => check.FlowStateBeforeCheck)
            .ToArray();

        Assert.Equal(
            [NullableFlowState.MaybeNull, NullableFlowState.NotNull, NullableFlowState.NotNull],
            states
        );
    }

    [Fact]
    public void Custom_equality_and_same_named_presence_properties_are_not_builtin_null_checks()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Checks",
            [
                new(
                    "C.cs",
                    """
                    class Custom {
                        public bool HasValue => true;
                        public static bool operator ==(Custom? a, Custom? b) => true;
                        public static bool operator !=(Custom? a, Custom? b) => false;
                        public override bool Equals(object? other) => false;
                        public override int GetHashCode() => 0;
                        void M(Custom value) { _ = value == null; _ = !value.HasValue; _ = value is null; }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var actual = OperationQueries
            .NullChecks.In(solution)
            .Select(check => check.Condition.Syntax.ToString())
            .ToArray();

        Assert.Equal(["value is null"], actual);
    }
}
