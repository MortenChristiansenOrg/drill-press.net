using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Relationships;

public sealed class InterfaceImplementationsTests(SemanticRuleFixture fixture)
    : IClassFixture<SemanticRuleFixture>
{
    [Theory]
    [InlineData("class C : I { }", "DP1003:I")]
    [InlineData("struct C : I { }", "DP1003:I")]
    [InlineData("abstract class C : I { }", "")]
    [InlineData("abstract class B : I { } class C : B { }", "DP1003:I")]
    [InlineData("class B : I { } class C : B { }", "")]
    [InlineData("partial class C : I { } partial class C { }", "DP1003:I")]
    public async Task Counts_concrete_definitions_once(string implementations, string expected)
    {
        var project = fixture.Project("interface I { } " + implementations);

        var findings = await fixture.Describe(project);

        Assert.Equal(
            expected,
            string.Join(",", findings.Select(finding => finding.Rule + ":" + finding.Text))
        );
    }

    [Fact]
    public async Task Constructed_generic_interfaces_and_partial_declarations_count_definitions_once()
    {
        var project = fixture.Project(
            "partial interface I<T> { } partial interface I<T> { } class C : I<string>, I<int> { }"
        );

        var findings = await fixture.Describe(project);

        Assert.Equal([("DP1003", 1, "I", (string?)null)], findings);
    }

    [Fact]
    public async Task Generated_implementations_count_without_becoming_candidates()
    {
        var project = fixture.Project(
            "interface I { }",
            extra:
            [
                new DocumentSnapshot(
                    "Generated.g.cs",
                    "class C : I { string Value => string.Empty; }",
                    true
                ),
            ]
        );

        var findings = await fixture.Describe(project);

        Assert.Equal([("DP1003", 1, "I", (string?)null)], findings);
    }

    [Fact]
    public async Task Source_dependency_is_counted_and_test_implementations_are_excluded()
    {
        var contracts = fixture.Project("public interface I { }", "Contracts");
        var product = fixture.Project("class C : I { }", "Product", dependencies: [contracts]);
        var tests = fixture.Project(
            "class Fake : I { }",
            "Tests",
            isTest: true,
            dependencies: [contracts]
        );

        var findings = await fixture.Describe(contracts, product, tests);

        Assert.Equal([("DP1003", 1, "I", (string?)null)], findings);
    }

    [Fact]
    public async Task Separate_compatible_consumers_are_counted_together()
    {
        var contracts = fixture.Project("public interface I { }", "Contracts");
        var first = fixture.Project("class First : I { }", "First", dependencies: [contracts]);
        var second = fixture.Project("class Second : I { }", "Second", dependencies: [contracts]);

        var findings = await fixture.Describe(contracts, first, second);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task Alternate_target_frameworks_have_independent_counts()
    {
        var first = fixture.Project("interface I { } class C : I { }", framework: "net9.0");
        var second = fixture.Project(
            "interface I { } class C : I { } class D : I { }",
            framework: "net10.0"
        );

        var response = await fixture.Evaluate(first, second);

        Assert.Equal([1, 0], response.Contexts.Select(context => context.Findings.Length));
        Assert.Equal(
            ["DP1003"],
            response
                .Contexts.SelectMany(context => context.Findings)
                .Select(finding => finding.RuleId)
        );
    }

    [Fact]
    public async Task Equal_assembly_and_type_names_do_not_merge_unrelated_contexts()
    {
        var first = fixture.Project("public interface I { }", "Contracts", framework: "net9.0");
        var second = fixture.Project("public interface I { }", "Contracts", framework: "net10.0");
        var consumer = fixture.Project("class C : I { }", "Consumer", dependencies: [second]);

        var response = await fixture.Evaluate(first, second, consumer);

        Assert.Equal([0, 1, 0], response.Contexts.Select(context => context.Findings.Length));
        Assert.Equal(
            ["DP1003"],
            response
                .Contexts.SelectMany(context => context.Findings)
                .Select(finding => finding.RuleId)
        );
    }

    [Theory]
    [InlineData("class Extra { }", "DP1003")]
    [InlineData("class Extra : I { }", "")]
    public async Task Equal_consumer_frameworks_cannot_union_alternate_dependency_contexts(
        string extraSource,
        string expected
    )
    {
        var contracts = fixture.Project("public interface I { }", "Contracts");
        var first = fixture.Project(
            "public class Implementation : I { }",
            "Dependency",
            framework: "net9.0",
            dependencies: [contracts]
        );
        var second = fixture.Project(
            "public class Implementation : I { }",
            "Dependency",
            framework: "net10.0",
            dependencies: [contracts]
        );
        var firstRoot = fixture.Project("class First { }", "First", dependencies: [first]);
        var secondRoot = fixture.Project("class Second { }", "Second", dependencies: [second]);
        var extra = fixture.Project(extraSource, "Extra", dependencies: [contracts]);

        var response = await fixture.Evaluate(
            contracts,
            first,
            second,
            firstRoot,
            secondRoot,
            extra
        );

        Assert.Equal(
            expected,
            string.Join(
                ",",
                response
                    .Contexts.SelectMany(context => context.Findings)
                    .Select(finding => finding.RuleId)
            )
        );
    }
}
