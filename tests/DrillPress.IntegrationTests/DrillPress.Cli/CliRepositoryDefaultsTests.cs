using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

[Collection(typeof(PackageCollection))]
public sealed class CliRepositoryDefaultsTests(PackageFixture fixture) : IntegrationTest
{
    [Fact]
    public async Task Committed_defaults_check_the_repository_from_a_subdirectory()
    {
        var repository = await fixture.CopyRepositorySampleAsync("defaults-subdirectory");

        var result = await fixture.DotnetAsync(
            FileSystem.Path.Combine(repository, "src", "Shop"),
            "tool",
            "run",
            "drillpress",
            "--",
            "check"
        );

        Assert.Equal(
            (
                1,
                """
                SHOP001 Use "" instead of string.Empty.
                Checkout.cs
                  +10:45
                SHOP002 Inline locals that are read once.
                Checkout.cs
                  +9:9

                """.ReplaceLineEndings("\n"),
                ""
            ),
            result
        );
    }

    [Theory]
    [InlineData(
        new[] { "check", "--select", "SHOP003" },
        1,
        """
            SHOP003 Read the time from an injected TimeProvider.
            src/Shop/Checkout.cs
              10:29

            """
    )]
    [InlineData(
        new[] { "check", "--ignore", "SHOP002" },
        1,
        """
            SHOP001 Use "" instead of string.Empty.
            src/Shop/Checkout.cs
              +10:45

            """
    )]
    [InlineData(new[] { "check", "--exclude", "src/" }, 0, "")]
    [InlineData(
        new[] { "status" },
        1,
        """
            src/Shop/Shop.csproj 2
              trivial 2

            """
    )]
    public async Task Command_line_options_override_the_committed_defaults(
        string[] arguments,
        int exitCode,
        string output
    )
    {
        var repository = await fixture.CopyRepositorySampleAsync(
            "defaults-override-" + string.Join("-", arguments).Replace("/", "")
        );

        var result = await fixture.DotnetAsync(
            repository,
            ["tool", "run", "drillpress", "--", .. arguments]
        );

        Assert.Equal((exitCode, output.ReplaceLineEndings("\n"), ""), result);
    }

    [Fact]
    public async Task Without_exclusions_the_rules_project_and_vendored_code_report_too()
    {
        var repository = await fixture.CopyRepositorySampleAsync("defaults-unexcluded");
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(repository, "everything.json"),
            """{ "rules": "eng/Rules", "target": "Shop.slnx" }""",
            TestContext.Current.CancellationToken
        );

        var result = await fixture.DotnetAsync(
            repository,
            "tool",
            "run",
            "drillpress",
            "--",
            "check",
            "--config",
            "everything.json"
        );

        Assert.Equal(
            (
                1,
                """
                SHOP001 Use "" instead of string.Empty.
                eng/Rules/Program.cs
                  7:46
                src/Shop/Checkout.cs
                  +10:45
                third_party/Vendor/VendorClock.cs
                  +12:23
                SHOP002 Inline locals that are read once.
                src/Shop/Checkout.cs
                  +9:9
                third_party/Vendor/VendorClock.cs
                  +10:9
                SHOP003 Read the time from an injected TimeProvider.
                eng/Rules/Program.cs
                  33:48
                src/Shop/Checkout.cs
                  10:29
                drillpress-allow-invalid Write allowances as // drillpress-allow RULE-ID[, RULE-ID]: reason, after or directly above the code.
                third_party/Vendor/VendorClock.cs
                  11:9 [missing reason]

                """.ReplaceLineEndings("\n"),
                ""
            ),
            result
        );
    }

    [Fact]
    public async Task Fix_uses_excluded_declarations_to_rewrite_reported_code_and_rechecks_clean()
    {
        var repository = await fixture.CopyRepositorySampleAsync("defaults-fix");
        var vendored = FileSystem.Path.Combine(
            repository,
            "third_party",
            "Vendor",
            "VendorClock.cs"
        );
        var original = await FileSystem.File.ReadAllTextAsync(
            vendored,
            TestContext.Current.CancellationToken
        );

        var fix = await fixture.DotnetAsync(repository, "tool", "run", "drillpress", "--", "fix");
        var check = await fixture.DotnetAsync(
            repository,
            "tool",
            "run",
            "drillpress",
            "--",
            "check"
        );
        var build = await fixture.DotnetAsync(
            repository,
            "build",
            FileSystem.Path.Combine("src", "Shop", "Shop.csproj"),
            "--no-restore"
        );

        Assert.Equal((0, "", ""), fix);
        Assert.Equal((0, "", ""), check);
        Assert.Equal(0, build.ExitCode);
        Assert.Equal(
            """
            using Vendor;

            namespace Shop;

            public static class Checkout
            {
                public static string Stamp()
                {
                    return VendorClock.Create().Format(DateTime.Now) + "";
                }
            }

            """.ReplaceLineEndings("\n"),
            await FileSystem.File.ReadAllTextAsync(
                FileSystem.Path.Combine(repository, "src", "Shop", "Checkout.cs"),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(
            original,
            await FileSystem.File.ReadAllTextAsync(vendored, TestContext.Current.CancellationToken)
        );
    }
}
