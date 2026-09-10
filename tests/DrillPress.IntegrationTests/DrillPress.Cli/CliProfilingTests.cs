using System.Text.Json;
using DrillPress.Manifest;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

public sealed class CliProfilingTests : IntegrationTest
{
    [Fact]
    public async Task Profile_reports_complete_process_phases_without_changing_compact_stdout()
    {
        var directory = CreateTemporaryDirectory("drillpress-profile-");
        var source = FileSystem.Path.Combine(directory.FullName, "A.cs");
        await FileSystem.File.WriteAllTextAsync(source, "public class A { public string Value => string.Empty; }", TestContext.Current.CancellationToken);
        string[] arguments = [GetOutputPath("DrillPress.Cli"), "check", "--build-host", GetOutputPath("DrillPress.BuildHost"),
            "--rules", GetOutputPath("DrillPress.SampleRules", "samples"), source];

        var plain = await RunProcessAsync("dotnet", arguments, RepositoryRoot, TestContext.Current.CancellationToken);
        var profiled = await RunProcessAsync("dotnet", [.. arguments, "--profile", "--no-optimization"], RepositoryRoot, TestContext.Current.CancellationToken);
        var events = ParseEvents(profiled.StandardError);

        Assert.Equal(1, plain.ExitCode);
        Assert.Equal(1, profiled.ExitCode);
        Assert.Equal("", plain.StandardError);
        Assert.Equal(plain.StandardOutput, profiled.StandardOutput);
        Assert.Equal([
            "cli.build-host", "build-host.loading", "build-host.snapshot.serialization", "build-host.snapshot.bytes", "build-host.contexts", "build-host.total",
            "cli.snapshot.loading", "cli.rules", "rules.snapshot.loading", "rules.reconstruction", "rules.preparation",
            "rules.rule.SDK2001", "rules.rule.SDK2002", "rules.rule.SDK2003", "rules.rule.SDK2004", "rules.rule.SDK2005", "rules.rule.SDK2006",
            "rules.rule.SDK2007", "rules.rule.SDK2008", "rules.rule.SDK2011", "rules.rule.SDK2012", "rules.rule.SDK2009", "rules.rule.SDK2010",
            "rules.rule.DP1001", "rules.rule.DP1002", "rules.rule.DP1003", "rules.rule.DP1004", "rules.rule.DP1005",
            "rules.member.symbol.bindings", "rules.interface.definition.comparisons", "rules.fix.validation", "rules.aggregation",
            "rules.contexts", "rules.context.findings", "rules.actionable.locations", "rules.common.safe.batches", "rules.common.safe.edits",
            "rules.response.serialization", "rules.total", "cli.public.bytes", "cli.rendering", "cli.total",
        ], events.Select(entry => entry.Component + "." + entry.Phase));
        Assert.Equal(1, events.Single(entry => entry.Component == "rules" && entry.Phase == "context.findings").Count);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(plain.StandardOutput), events.Single(entry => entry.Phase == "public.bytes").Count);
        Assert.All(events, entry => Assert.True(entry.WallMilliseconds >= 0 && entry.UserCpuMilliseconds >= 0 && entry.SystemCpuMilliseconds >= 0));
    }

    private static ProfileEvent[] ParseEvents(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => JsonSerializer.Deserialize(line["drillpress-profile ".Length..], CompilationSnapshotJsonContext.Default.ProfileEvent)!).ToArray();
}
