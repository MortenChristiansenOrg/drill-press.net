using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class ResultValueTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    private const string Members = """
        using System;
        #nullable enable
        public class Input {
            private static bool Choose() => DateTime.UtcNow.Ticks % 2 == 0;
            private static string? Maybe() => null;
            private static string Sure() => "";
            private static int Number() => 1;
            private static object Box() => 1;
        """;

    [Theory]
    [InlineData("string[]", "[]", "[] -> Construction Direct non-null")]
    [InlineData("object", "new()", "new() -> Construction Direct non-null")]
    [InlineData("string[]", "new string[0]", "new string[0] -> Construction Direct non-null")]
    [InlineData("string", "\"\"", "\"\" -> Constant Direct non-null")]
    [InlineData("string", "$\"{Number()}\"", "$\"{Number()}\" -> Construction Direct non-null")]
    [InlineData("int", "default", "default -> ValueTypeDefault Direct non-null")]
    [InlineData("int", "default(int)", "default(int) -> ValueTypeDefault Direct non-null")]
    [InlineData("Guid", "default", "default -> ValueTypeDefault Direct non-null")]
    [InlineData("Guid", "default(Guid)", "default(Guid) -> ValueTypeDefault Direct non-null")]
    [InlineData("int", "Number()", "Number() -> ValueTypeValue Direct non-null")]
    [InlineData("int?", "Number()", "Number() -> ValueTypeValue Direct non-null")]
    [InlineData("int?", "new()", "new() -> Constant Direct non-null")]
    [InlineData("Guid?", "new()", "new() -> Construction Direct non-null")]
    [InlineData("int", "(int)Box()", "(int)Box() -> ValueTypeValue Direct non-null")]
    [InlineData("string", "null!", "null! -> null Null Direct suppressed")]
    [InlineData("string", "default!", "default! -> default ReferenceDefault Direct suppressed")]
    [InlineData(
        "string",
        "default(string)!",
        "default(string)! -> default(string) ReferenceDefault Direct suppressed"
    )]
    [InlineData("int?", "default", "default -> Null Direct")]
    [InlineData("int?", "new int?()", "new int?() -> Null Direct")]
    [InlineData("int?", "null", "null -> Null Direct")]
    [InlineData("string", "Maybe()!", "Maybe()! -> Maybe() Unproven Direct suppressed")]
    [InlineData("string", "(Maybe())!", "(Maybe())! -> Maybe() Unproven Direct suppressed")]
    [InlineData("string", "Sure()", "Sure() -> Unproven Direct")]
    [InlineData("string", "\"\"!", "\"\"! -> \"\" Constant Direct suppressed non-null")]
    [InlineData("string?", "(string?)null", "(string?)null -> null Null Direct cast")]
    [InlineData("object", "(object)\"\"", "(object)\"\" -> \"\" Constant Direct cast non-null")]
    public void Classifies_a_single_result(string type, string value, string expected)
    {
        var analysis = PropertyResults(type, value);

        Assert.Equal(ExpressionTraversalStatus.Complete, analysis.Status);
        Assert.Equal([expected], Describe(analysis));
    }

    [Fact]
    public void Conditional_and_switch_branches_are_results_while_conditions_are_not()
    {
        var branch = PropertyResults("string", "Choose() ? Maybe()! : \"\"");
        var selection = PropertyResults("string", "Choose() switch { true => null!, _ => \"\" }");

        Assert.Equal(
            ["Maybe()! -> Maybe() Unproven Direct suppressed", "\"\" -> Constant Direct non-null"],
            Describe(branch)
        );
        Assert.False(branch.IsNonNullByLanguage);
        Assert.Equal(
            ["null! -> null Null Direct suppressed", "\"\" -> Constant Direct non-null"],
            Describe(selection)
        );
        Assert.False(selection.IsNonNullByLanguage);
    }

    [Fact]
    public void Coalescing_separates_the_non_null_left_outcome_from_the_fallback()
    {
        var safe = PropertyResults("string", "Maybe() ?? \"\"");
        var unsafeFallback = PropertyResults("string", "Maybe() ?? null!");
        var nested = PropertyResults("string", "(Maybe() ?? Maybe()) ?? \"\"");

        Assert.Equal(
            ["Maybe() -> Unproven NonNullLeft non-null", "\"\" -> Constant Fallback non-null"],
            Describe(safe)
        );
        Assert.True(safe.IsNonNullByLanguage);
        Assert.Equal(
            ["Maybe() -> Unproven NonNullLeft non-null", "null! -> null Null Fallback suppressed"],
            Describe(unsafeFallback)
        );
        Assert.False(unsafeFallback.IsNonNullByLanguage);
        Assert.Equal(
            [
                "Maybe() -> Unproven NonNullLeft non-null",
                "Maybe() -> Unproven NonNullLeft non-null",
                "\"\" -> Constant Fallback non-null",
            ],
            Describe(nested)
        );
        Assert.True(nested.IsNonNullByLanguage);
    }

    [Fact]
    public void Throw_arms_do_not_supply_results()
    {
        var analysis = PropertyResults("string", "Maybe() ?? throw new Exception()");

        Assert.Equal(["Maybe() -> Unproven NonNullLeft non-null"], Describe(analysis));
        Assert.True(analysis.IsNonNullByLanguage);
    }

    [Fact]
    public void Suppression_keeps_the_declared_annotation_and_flags_masked_flow_state()
    {
        var value = PropertyResults("string", "Maybe()!").Values.Single();

        Assert.Equal(NullableAnnotation.Annotated, value.Expression.DeclaredNullability);
        Assert.Equal(NullableFlowState.NotNull, value.Expression.FlowState);
        Assert.True(value.IsSuppressed);
        Assert.False(value.IsNonNullByLanguage);
    }

    [Theory]
    [InlineData("string[]", "[]", "[] -> Construction Direct non-null")]
    [InlineData("object", "new()", "new() -> Construction Direct non-null")]
    [InlineData("string[]", "new string[0]", "new string[0] -> Construction Direct non-null")]
    [InlineData("string", "\"\"", "\"\" -> Constant Direct non-null")]
    [InlineData("int", "default(int)", "default(int) -> ValueTypeDefault Direct non-null")]
    [InlineData("Guid", "default(Guid)", "default(Guid) -> ValueTypeDefault Direct non-null")]
    [InlineData("string", "default", "default -> ReferenceDefault Direct")]
    [InlineData("string", "null", "null -> Null Direct")]
    public void Language_facts_survive_with_nullable_analysis_disabled(
        string type,
        string value,
        string expected
    )
    {
        var analysis = Results(
            $$"""
            using System;
            #nullable disable
            public class Input { public {{type}} Value { get; init; } = {{value}}; }
            """
        );

        Assert.Equal(ExpressionTraversalStatus.Complete, analysis.Status);
        Assert.Equal([expected], Describe(analysis));
    }

    [Fact]
    public void Parameter_defaults_are_classified_like_initializers()
    {
        var solution = Analyze(
            """
            using System;
            #nullable enable
            public record RecordInput(Guid Id = default, string Label = "", string Missing = null!);
            """
        );

        var results = Code
            .Parameters.In(solution)
            .Select(parameter => parameter.DefaultValue!.ResultValues())
            .ToArray();

        Assert.Equal(
            [
                "default -> ValueTypeDefault Direct non-null",
                "\"\" -> Constant Direct non-null",
                "null! -> null Null Direct suppressed",
            ],
            results.SelectMany(Describe)
        );
    }

    [Fact]
    public void Default_of_an_unconstrained_type_parameter_stays_undecided()
    {
        var analysis = Results(
            """
            #nullable enable
            public class Input<T> { public T Value { get; init; } = default!; }
            """
        );

        Assert.Equal(
            ["default! -> default TypeParameterDefault Direct suppressed"],
            Describe(analysis)
        );
        Assert.False(analysis.IsNonNullByLanguage);
    }

    [Theory]
    [InlineData("Other", "\"\"", "\"\"")]
    [InlineData("Other", "(Other)\"\"", "(Other)\"\"")]
    [InlineData("Other", "(\"\")", "(\"\")")]
    [InlineData("Other", "\"\"!", "\"\"!")]
    [InlineData("Other", "Text() ?? new Other()", "Text() ?? new Other()")]
    [InlineData("Other", "Choose() ? 1 : \"\"", "1")]
    public void User_conversions_are_boundaries(string type, string value, string boundary)
    {
        var analysis = Results(
            $$"""
            #nullable enable
            public class Other {
                public static implicit operator Other(string text) => new();
                public static implicit operator Other(int number) => new();
            }
            public class Input {
                private static bool Choose() => true;
                private static string? Text() => null;
                public {{type}} Value { get; init; } = {{value}};
            }
            """,
            property => property.Name == "Value"
        );

        Assert.Equal(ExpressionTraversalStatus.Unavailable, analysis.Status);
        Assert.Contains(
            analysis.Boundaries,
            stop =>
                stop.Reason == ExpressionTraversalReason.UnsupportedExpression
                && stop.Expression.Syntax.ToString() == boundary
        );
        Assert.False(analysis.IsNonNullByLanguage);
    }

    [Theory]
    [InlineData("T", "new T() -> Unproven Direct")]
    [InlineData("TStruct", "new TStruct() -> Construction Direct non-null")]
    [InlineData("TClass", "new TClass() -> Construction Direct non-null")]
    public void Type_parameter_creation_counts_only_when_nullable_values_are_excluded(
        string type,
        string expected
    )
    {
        var analysis = Results(
            $$"""
            #nullable enable
            public class Base {}
            public class Input<T, TStruct, TClass>
                where T : new()
                where TStruct : struct
                where TClass : Base, new()
            {
                public {{type}} Value { get; init; } = new {{type}}();
            }
            """
        );

        Assert.Equal([expected], Describe(analysis));
    }

    [Theory]
    [InlineData("Built", "[] -> Unproven Direct")]
    [InlineData("BuiltValue", "[] -> Construction Direct non-null")]
    [InlineData("List<int>", "[] -> Construction Direct non-null")]
    [InlineData("IEnumerable<int>", "[] -> Construction Direct non-null")]
    [InlineData("int[]", "[] -> Construction Direct non-null")]
    public void Collection_builders_are_not_language_proven(string type, string expected)
    {
        var analysis = Results(
            $$"""
            #nullable enable
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            [CollectionBuilder(typeof(Maker), nameof(Maker.Create))]
            public class Built : IEnumerable<int> {
                public IEnumerator<int> GetEnumerator() => throw new Exception();
                IEnumerator IEnumerable.GetEnumerator() => throw new Exception();
            }
            [CollectionBuilder(typeof(Maker), nameof(Maker.CreateValue))]
            public struct BuiltValue : IEnumerable<int> {
                public IEnumerator<int> GetEnumerator() => throw new Exception();
                IEnumerator IEnumerable.GetEnumerator() => throw new Exception();
            }
            public static class Maker {
                public static Built Create(ReadOnlySpan<int> items) => null!;
                public static BuiltValue CreateValue(ReadOnlySpan<int> items) => default;
            }
            public class Input { public {{type}} Value { get; init; } = []; }
            """,
            property => property.Name == "Value"
        );

        Assert.Equal([expected], Describe(analysis));
    }

    [Fact]
    public void Invalid_binding_and_dynamic_values_are_boundaries()
    {
        var invalid = Results(
            """
            public class Input { public string Value { get; init; } = Missing(); }
            """
        );
        var dynamicValue = Results(
            """
            #nullable enable
            public class Input {
                private static dynamic Open() => 1;
                public object Value { get; init; } = Open();
            }
            """,
            property => property.Name == "Value"
        );

        Assert.Equal(
            [("Missing()", ExpressionTraversalReason.InvalidBinding)],
            invalid.Boundaries.Select(stop => (stop.Expression.Syntax.ToString(), stop.Reason))
        );
        Assert.Equal(ExpressionTraversalStatus.Unavailable, invalid.Status);
        Assert.Equal(
            [("Open()", ExpressionTraversalReason.UnsupportedExpression)],
            dynamicValue.Boundaries.Select(stop => (stop.Expression.Syntax.ToString(), stop.Reason))
        );
    }

    [Fact]
    public void Bounds_are_reported_rather_than_treated_as_success()
    {
        var expression = Property("string", "Choose() ? (Choose() ? \"\" : \"a\") : \"b\"");

        var deep = expression.ResultValues(maxDepth: 1);
        var wide = expression.ResultValues(maxResults: 1);

        Assert.Equal(ExpressionTraversalStatus.LimitExceeded, deep.Status);
        Assert.Equal(
            [ExpressionTraversalReason.DepthLimit],
            deep.Boundaries.Select(stop => stop.Reason)
        );
        Assert.False(deep.IsNonNullByLanguage);
        Assert.Equal(ExpressionTraversalStatus.LimitExceeded, wide.Status);
        Assert.Equal(
            [ExpressionTraversalReason.ExpressionLimit],
            wide.Boundaries.Select(stop => stop.Reason)
        );
        Assert.False(wide.IsNonNullByLanguage);
    }

    [Fact]
    public void Invalid_bounds_are_rejected()
    {
        var expression = Property("string", "\"\"");

        Assert.Throws<ArgumentOutOfRangeException>(() => expression.ResultValues(maxDepth: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => expression.ResultValues(maxResults: 0));
    }

    private ResultValueAnalysis PropertyResults(string type, string value) =>
        Property(type, value).ResultValues();

    private CodeExpression Property(string type, string value)
    {
        var solution = Analyze(
            $$"""
            {{Members}}
                public {{type}} Value { get; init; } = {{value}};
            }
            """
        );
        return Code.Properties.Named("Value").In(solution).Single().Initializer!;
    }

    private ResultValueAnalysis Results(string text, Func<CodeProperty, bool>? where = null)
    {
        var properties = Code.Properties.In(Analyze(text));
        return properties.Single(where ?? (_ => true)).Initializer!.ResultValues();
    }

    private static IEnumerable<string> Describe(ResultValueAnalysis analysis) =>
        analysis.Values.Select(value =>
            string.Join(
                " ",
                new[]
                {
                    value.Anchor.Syntax.ToString(),
                    "->",
                    value.Anchor.Syntax == value.Expression.Syntax
                        ? null
                        : value.Expression.Syntax.ToString(),
                    value.Kind.ToString(),
                    value.Role.ToString(),
                    value.IsSuppressed ? "suppressed" : null,
                    value.IsCast ? "cast" : null,
                    value.IsNonNullByLanguage ? "non-null" : null,
                }.OfType<string>()
            )
        );

    private AnalysisSolution Analyze(string text)
    {
        var workspace = fixture.Workspace();
        workspace.AddProject("Inputs", [new("Inputs.cs", text)], allowErrors: true);
        return workspace.Analyze(TestContext.Current.CancellationToken);
    }
}
