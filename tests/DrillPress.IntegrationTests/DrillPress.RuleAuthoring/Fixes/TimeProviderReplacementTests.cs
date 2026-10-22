using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class TimeProviderReplacementTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task A_same_named_user_property_is_not_a_framework_clock_read()
    {
        var workspace = fixture.Workspace();
        const string source = """
            namespace Custom;
            class DateTimeOffset { public static System.DateTimeOffset UtcNow => default; }
            class A(System.TimeProvider clock) { System.DateTimeOffset Now() => DateTimeOffset.UtcNow; }
            """;
        workspace.AddProject("A", [new("A.cs", source)]);
        var rules = new RuleCatalog();
        rules
            .Rule("READ", "Use the clock.")
            .For(CodeType.Named("Custom.DateTimeOffset").Member("UtcNow").References)
            .Forbid(fix: reference => Fix.For(reference).UseTimeProvider("clock").Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal("READ Use the clock.\nA.cs\n  3:69\n", result.Output);
        Assert.Equal(source, result.FixedText("A.cs"));
    }

    [Fact]
    public async Task Clock_reads_and_waits_use_the_selected_injected_provider()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    using System;
                    using System.Threading.Tasks;
                    class A(TimeProvider clock) {
                        DateTimeOffset Now() => DateTimeOffset.UtcNow;
                        Task Wait() => Task.Delay(TimeSpan.FromSeconds(1));
                    }
                    """
                ),
            ]
        );
        var rules = new RuleCatalog();
        rules
            .Rule("READ", "Use the clock.")
            .For(CodeType.Of<DateTimeOffset>().Member("UtcNow").References)
            .Forbid(fix: reference => Fix.For(reference).UseTimeProvider("clock").Propose());
        rules
            .Rule("WAIT", "Use the clock.")
            .For(Code.Calls.ToMethodsNamed("Delay").Where(TimeProviderFacts.WaitsWithoutClock))
            .Forbid(fix: call => Fix.For(call).UseTimeProvider("clock").Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            using System;
            using System.Threading.Tasks;
            class A(TimeProvider clock) {
                DateTimeOffset Now() => clock.GetUtcNow();
                Task Wait() => Task.Delay(TimeSpan.FromSeconds(1), clock);
            }
            """,
            result.FixedText("A.cs")
        );
        Assert.Equal([true, true], result.Findings.Select(finding => finding.HasFix).ToArray());
    }

    [Fact]
    public async Task A_local_DateTime_kind_is_not_changed_by_clock_replacement()
    {
        var workspace = fixture.Workspace();
        const string source =
            "using System; class A(TimeProvider clock) { DateTime Now() => DateTime.Now; }";
        workspace.AddProject("A", [new("A.cs", source)]);
        var rules = new RuleCatalog();
        rules
            .Rule("READ", "Use the clock.")
            .For(CodeType.Of<DateTime>().Member("Now").References)
            .Forbid(fix: reference => Fix.For(reference).UseTimeProvider("clock").Propose());

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal([false], result.Findings.Select(finding => finding.HasFix).ToArray());
        Assert.Equal(source, result.FixedText("A.cs"));
    }
}
