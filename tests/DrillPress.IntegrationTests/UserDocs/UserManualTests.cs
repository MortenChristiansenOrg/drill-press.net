using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.UserDocs;

public sealed class UserManualTests(UserManualFixture fixture) : IClassFixture<UserManualFixture>
{
    public static TheoryData<string, string, string> Examples => UserManualFixture.ReadExamples();

    [Theory]
    [MemberData(nameof(Examples))]
    public void Complete_examples_compile_against_the_public_SDK(
        string name,
        string kind,
        string code
    )
    {
        var errors = fixture.Compile(name, kind, code);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Union_example_reports_once_per_method_and_fixes_every_source_form()
    {
        var rules = fixture.CreateRules("selections.html#example-union", "ClockRules");
        var workspace = new RuleTestWorkspace();
        workspace.AddProject(
            "Clock",
            [
                new(
                    "Clock.cs",
                    """
                    using System;
                    using System.Threading.Tasks;
                    class Clock(TimeProvider clock)
                    {
                        DateTimeOffset Created = DateTimeOffset.UtcNow;
                        DateTimeOffset Updated = DateTimeOffset.UtcNow;

                        async Task<DateTimeOffset> Later()
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            return DateTimeOffset.UtcNow;
                        }
                    }
                    """
                ),
            ]
        );

        var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken);

        Assert.Equal(
            """
            TEAM040 Read time through the injected TimeProvider.
            Clock.cs
              +5:30
              +6:30
              +10:20

            """.Replace("\r\n", "\n"),
            result.Output
        );
        Assert.Equal(
            """
            using System;
            using System.Threading.Tasks;
            class Clock(TimeProvider clock)
            {
                DateTimeOffset Created = clock.GetUtcNow();
                DateTimeOffset Updated = clock.GetUtcNow();

                async Task<DateTimeOffset> Later()
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), clock);
                    return clock.GetUtcNow();
                }
            }
            """,
            result.FixedText("Clock.cs")
        );
    }

    [Fact]
    public void Local_navigation_and_assets_resolve()
    {
        var broken = fixture.BrokenLinks();

        Assert.Empty(broken);
    }

    [Fact]
    public void Manual_contains_checked_examples()
    {
        var examples = Examples;

        Assert.NotEmpty(examples);
    }
}
