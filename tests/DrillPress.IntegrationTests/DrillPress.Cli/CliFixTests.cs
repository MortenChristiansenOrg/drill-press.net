using System.Text;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

public sealed class CliFixTests : IntegrationTest
{
    [Fact]
    public async Task Real_processes_apply_both_safe_rules_across_files_and_recheck_clean()
    {
        var directory = CreateTemporaryDirectory("drillpress-fix-");
        var first = FileSystem.Path.Combine(directory.FullName, "A.cs");
        var second = FileSystem.Path.Combine(directory.FullName, "B.cs");
        const string emptySource = "public class A { public string Value => string.Empty; }\r\n";
        const string ordinalSource = "using System;\nusing System.Linq;\r\npublic class B { public object Values => new[] { \"é😀\" }.Distinct(StringComparer.Ordinal); }\n";
        await FileSystem.File.WriteAllTextAsync(first, emptySource, new UnicodeEncoding(false, true, true), TestContext.Current.CancellationToken);
        await FileSystem.File.WriteAllTextAsync(second, ordinalSource, new UTF8Encoding(false, true), TestContext.Current.CancellationToken);

        var result = await FixAsync(FileSystem.Path.Combine(directory.FullName, "*.cs"));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal("", result.StandardError);
        Assert.Equal<byte>([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("public class A { public string Value => \"\"; }\r\n")], FileSystem.File.ReadAllBytes(first));
        Assert.Equal(Encoding.UTF8.GetBytes("using System;\nusing System.Linq;\r\npublic class B { public object Values => new[] { \"é😀\" }.Distinct(); }\n"), FileSystem.File.ReadAllBytes(second));
        Assert.Equal([first, second], FileSystem.Directory.GetFiles(directory.FullName).Order());
    }

    [Fact]
    public async Task Sample_solution_builds_after_all_offered_fixes_are_applied()
    {
        var directory = CreateTemporaryDirectory("drillpress-fixed-sample-");
        var project = CopySampleSolution(directory.FullName);
        await RestoreAsync(project);

        var result = await FixAsync(project);
        var build = await RunProcessAsync("dotnet", ["build", project, "--no-restore", "--nologo"], directory.FullName, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardError);
        Assert.DoesNotContain("DP1004", result.StandardOutput);
        Assert.Contains("DP1003", result.StandardOutput);
        Assert.Contains("DP1005", result.StandardOutput);
        Assert.Equal(0, build.ExitCode);
        Assert.Empty(FileSystem.Directory.GetFiles(directory.FullName, ".dp-*.tmp"));
    }

    private static string CopySampleSolution(string destination)
    {
        var root = RepositoryPath("Sample Solution");
        foreach (var source in FileSystem.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !FileSystem.Path.GetRelativePath(root, path).Split(FileSystem.Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            var target = FileSystem.Path.Combine(destination, FileSystem.Path.GetRelativePath(root, source));
            FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(target)!);
            FileSystem.File.Copy(source, target);
        }

        return FileSystem.Path.Combine(destination, "DrillPress.SampleTarget.slnx");
    }

    private Task<ProcessResult> FixAsync(string target) => RunProcessAsync("dotnet",
        [GetOutputPath("DrillPress.Cli"), "fix", "--build-host", GetOutputPath("DrillPress.BuildHost"),
            "--rules", GetOutputPath("DrillPress.SampleRules", "samples"), target], RepositoryRoot, TestContext.Current.CancellationToken);
}
