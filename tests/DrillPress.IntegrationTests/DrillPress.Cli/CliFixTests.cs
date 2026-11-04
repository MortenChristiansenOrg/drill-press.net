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
        const string ordinalSource =
            "using System;\nusing System.Linq;\r\npublic class B { public object Values => new[] { \"é😀\" }.Distinct(StringComparer.Ordinal); }\n";
        await FileSystem.File.WriteAllTextAsync(
            first,
            emptySource,
            new UnicodeEncoding(false, true, true),
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            second,
            ordinalSource,
            new UTF8Encoding(false, true),
            TestContext.Current.CancellationToken
        );

        var result = await FixAsync(FileSystem.Path.Combine(directory.FullName, "*.cs"));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal("", result.StandardError);
        Assert.Equal<byte>(
            [
                .. Encoding.Unicode.GetPreamble(),
                .. Encoding.Unicode.GetBytes("public class A { public string Value => \"\"; }\r\n"),
            ],
            FileSystem.File.ReadAllBytes(first)
        );
        Assert.Equal(
            Encoding.UTF8.GetBytes(
                "using System;\nusing System.Linq;\r\npublic class B { public object Values => new[] { \"é😀\" }.Distinct(); }\n"
            ),
            FileSystem.File.ReadAllBytes(second)
        );
        Assert.Equal([first, second], FileSystem.Directory.GetFiles(directory.FullName).Order());
    }

    [Fact]
    public async Task Real_processes_keep_allowed_findings_and_remove_unused_allowances()
    {
        var directory = CreateTemporaryDirectory("drillpress-allowance-fix-");
        var path = FileSystem.Path.Combine(directory.FullName, "A.cs");
        await FileSystem.File.WriteAllTextAsync(
            path,
            """
            public class A
            {
                public string Value => string.Empty; // drillpress-allow DP1004: the contract test compares it
                public string Other => string.Empty;
                // drillpress-allow DP1005: nothing below passes a comparer
                public int Count => 1;
            }

            """,
            TestContext.Current.CancellationToken
        );

        var result = await FixAsync(path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal("", result.StandardError);
        Assert.Equal(
            """
            public class A
            {
                public string Value => string.Empty; // drillpress-allow DP1004: the contract test compares it
                public string Other => "";
                public int Count => 1;
            }

            """,
            FileSystem.File.ReadAllText(path)
        );
    }

    [Fact]
    public async Task Sample_solution_builds_after_all_offered_fixes_are_applied()
    {
        var directory = CreateTemporaryDirectory("drillpress-fixed-sample-");
        var project = CopySampleSolution(directory.FullName);
        await RestoreAsync(project);

        var result = await FixAsync(project);
        var build = await RunProcessAsync(
            "dotnet",
            ["build", project, "--no-restore", "--nologo"],
            directory.FullName,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardError);
        Assert.DoesNotContain("DP1004", result.StandardOutput);
        Assert.Contains("DP1003", result.StandardOutput);
        Assert.Contains("DP1005", result.StandardOutput);
        Assert.Equal(0, build.ExitCode);
        Assert.Empty(FileSystem.Directory.GetFiles(directory.FullName, ".dp-*.tmp"));
    }

    [Fact]
    public async Task Real_processes_rename_and_move_files_with_their_edits_and_recheck_clean()
    {
        var root = await WriteLayoutSolutionAsync("drillpress-move-fix-");
        var solution = FileSystem.Path.Combine(root, "Orders.slnx");
        await RestoreAsync(solution);

        var result = await FixAsync(solution);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal("", result.StandardError);
        Assert.Equal(
            [
                "src/Orders/Invoice.cs",
                "src/Orders/Orders.csproj",
                "src/Orders/Services/OrderService.cs",
                "tests/Orders.Tests/Orders.Tests.csproj",
                "tests/Orders.Tests/Services/OrderServiceTests.cs",
            ],
            SourceFiles(root)
        );
        Assert.Equal(
            "namespace Orders;\n\npublic class Invoice { public string Number => \"\"; }\n",
            await FileSystem.File.ReadAllTextAsync(
                FileSystem.Path.Combine(root, "src", "Orders", "Invoice.cs"),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(
            "namespace Orders.Tests;\n\npublic class OrderServiceTests {}\n",
            await FileSystem.File.ReadAllTextAsync(
                FileSystem.Path.Combine(
                    root,
                    "tests",
                    "Orders.Tests",
                    "Services",
                    "OrderServiceTests.cs"
                ),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task A_move_to_a_path_created_after_analysis_stops_before_any_write()
    {
        var root = await WriteLayoutSolutionAsync("drillpress-move-collision-");
        var solution = FileSystem.Path.Combine(root, "Orders.slnx");
        await RestoreAsync(solution);
        var blocked = FileSystem.Path.Combine(root, "tests", "Orders.Tests", "Services");
        FileSystem.Directory.CreateDirectory(blocked);
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(blocked, "Services.csproj"),
            "<Project />",
            TestContext.Current.CancellationToken
        );

        var result = await FixAsync(solution);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal(
            $"""
            drillpress: Fix stopped: Cannot move to '{FileSystem.Path.Combine(
                blocked,
                "OrderServiceTests.cs"
            )}': '{blocked}' contains another project.
              failed: {System.Text.Json.JsonEncodedText.Encode(
                FileSystem.Path.Combine(root, "tests", "Orders.Tests", "OrderServiceTests.cs")
            )}
              pending: {System.Text.Json.JsonEncodedText.Encode(
                FileSystem.Path.Combine(root, "src", "Orders", "Misnamed.cs")
            )}

            """.ReplaceLineEndings(
                Environment.NewLine
            ),
            result.StandardError
        );
        Assert.Contains("src/Orders/Misnamed.cs", SourceFiles(root));
    }

    // A production project with a misnamed file and a test project whose test is outside the mirrored folder.
    private async Task<string> WriteLayoutSolutionAsync(string prefix)
    {
        var root = CreateTemporaryDirectory(prefix).FullName;
        var files = new Dictionary<string, string>
        {
            ["Orders.slnx"] = """
                <Solution>
                  <Project Path="src/Orders/Orders.csproj" />
                  <Project Path="tests/Orders.Tests/Orders.Tests.csproj" />
                </Solution>
                """,
            ["src/Orders/Orders.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """,
            ["src/Orders/Misnamed.cs"] =
                "namespace Orders;\n\npublic class Invoice { public string Number => string.Empty; }\n",
            ["src/Orders/Services/OrderService.cs"] =
                "namespace Orders.Services;\n\npublic class OrderService {}\n",
            ["tests/Orders.Tests/Orders.Tests.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <IsTestProject>true</IsTestProject>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../../src/Orders/Orders.csproj" />
                  </ItemGroup>
                </Project>
                """,
            ["tests/Orders.Tests/OrderServiceTests.cs"] =
                "namespace Orders.Tests;\n\npublic class OrderServiceTests {}\n",
        };
        foreach (var (path, text) in files)
        {
            var full = FileSystem.Path.Combine(root, path);
            FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(full)!);
            await FileSystem.File.WriteAllTextAsync(
                full,
                text.ReplaceLineEndings("\n"),
                TestContext.Current.CancellationToken
            );
        }
        return root;
    }

    private static string[] SourceFiles(string root) =>
        FileSystem
            .Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Select(path => FileSystem.Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path =>
                !path.Split('/').Any(part => part is "bin" or "obj")
                && (
                    path.EndsWith(".cs", StringComparison.Ordinal)
                    || path.EndsWith("proj", StringComparison.Ordinal)
                )
            )
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string CopySampleSolution(string destination)
    {
        var root = RepositoryPath("Sample Solution");
        foreach (
            var source in FileSystem
                .Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path =>
                    !FileSystem
                        .Path.GetRelativePath(root, path)
                        .Split(FileSystem.Path.DirectorySeparatorChar)
                        .Any(part => part is "bin" or "obj")
                )
        )
        {
            var target = FileSystem.Path.Combine(
                destination,
                FileSystem.Path.GetRelativePath(root, source)
            );
            FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(target)!);
            FileSystem.File.Copy(source, target);
        }

        return FileSystem.Path.Combine(destination, "DrillPress.SampleTarget.slnx");
    }

    private Task<ProcessResult> FixAsync(string target, params string[] options) =>
        RunProcessAsync(
            "dotnet",
            [
                GetOutputPath("DrillPress.Cli"),
                "fix",
                "--build-host",
                GetOutputPath("DrillPress.BuildHost"),
                "--rules",
                GetOutputPath("DrillPress.SampleRules", "samples"),
                target,
                .. options,
            ],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );

    [Fact]
    public async Task Real_processes_fix_only_selected_complexity_and_ignore_remaining_unselected_findings()
    {
        var directory = CreateTemporaryDirectory("drillpress-complexity-fix-");
        var first = FileSystem.Path.Combine(directory.FullName, "A.cs");
        var second = FileSystem.Path.Combine(directory.FullName, "B.cs");
        const string unclassified = "public interface IContract { } public class B : IContract { }";
        await FileSystem.File.WriteAllTextAsync(
            first,
            "public class A { public string Value => string.Empty; }",
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            second,
            unclassified,
            TestContext.Current.CancellationToken
        );

        var result = await FixAsync(
            FileSystem.Path.Combine(directory.FullName, "*.cs"),
            "--fix-complexity",
            "trivial",
            "--show-fix-complexity"
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal("", result.StandardError);
        Assert.Equal(
            "public class A { public string Value => \"\"; }",
            FileSystem.File.ReadAllText(first)
        );
        Assert.Equal(unclassified, FileSystem.File.ReadAllText(second));
        Assert.Equal([first, second], FileSystem.Directory.GetFiles(directory.FullName).Order());
    }
}
