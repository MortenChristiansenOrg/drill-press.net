using System.Text.Json;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

public sealed class CliWorkerTests : IntegrationTest
{
    [Fact]
    public async Task Project_edits_reuse_unaffected_generations_and_match_a_fresh_check()
    {
        var root = CreateTemporaryDirectory("drillpress-worker-projects-").FullName;
        await Task.WhenAll(
            WriteProjectAsync(
                root,
                "A",
                "",
                "public static class A { public static string Value => \"\"; }"
            ),
            WriteProjectAsync(
                root,
                "B",
                "<ProjectReference Include=\"../A/A.csproj\" />",
                "public static class B { public static string Value => A.Value; }"
            ),
            WriteProjectAsync(root, "C", "", "public static class C {}")
        );
        var target = FileSystem.Path.Combine(root, "Target.slnx");
        await FileSystem.File.WriteAllTextAsync(
            target,
            "<Solution><Project Path=\"A/A.csproj\" /><Project Path=\"B/B.csproj\" /><Project Path=\"C/C.csproj\" /></Solution>",
            TestContext.Current.CancellationToken
        );
        await RestoreAsync(target);
        string[] arguments =
        [
            GetOutputPath("DrillPress.Cli"),
            "check",
            "--worker",
            "--profile",
            "--parallel-queries",
            "--build-host",
            GetOutputPath("DrillPress.BuildHost"),
            "--rules",
            GetOutputPath("DrillPress.SampleRules", "samples"),
            target,
        ];

        var first = await RunProcessAsync(
            "dotnet",
            arguments,
            root,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(root, "A", "A.cs"),
            "public static class A { public static string Value => string.Empty; }",
            TestContext.Current.CancellationToken
        );
        var edited = await RunProcessAsync(
            "dotnet",
            arguments,
            root,
            TestContext.Current.CancellationToken
        );
        var fresh = await RunProcessAsync(
            "dotnet",
            arguments.Where(argument => argument != "--worker").ToArray(),
            root,
            TestContext.Current.CancellationToken
        );
        var stopped = await RunProcessAsync(
            "dotnet",
            [
                arguments[0],
                "stop-workers",
                .. arguments[2..].Where(argument => argument != "--profile"),
            ],
            root,
            TestContext.Current.CancellationToken
        );

        Assert.Equal((0, ""), (first.ExitCode, first.StandardOutput));
        Assert.Equal(["loading.reused.C"], ReusedGenerations(edited.StandardError));
        Assert.Equal((1, fresh.StandardOutput), (edited.ExitCode, edited.StandardOutput));
        Assert.Equal(
            """
            DP1004 Use the empty string literal "" instead of string.Empty.
            A/A.cs
              +1:55

            """.ReplaceLineEndings("\n"),
            edited.StandardOutput
        );
        Assert.Equal(
            (0, "", ""),
            (stopped.ExitCode, stopped.StandardOutput, stopped.StandardError)
        );
    }

    [Fact]
    public async Task Workers_apply_a_directory_build_file_created_above_the_project()
    {
        var directory = CreateTemporaryDirectory("drillpress-worker-import-");
        var project = FileSystem.Path.Combine(directory.FullName, "src", "Target.csproj");
        FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(project)!);
        await FileSystem.File.WriteAllTextAsync(
            project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory.FullName, "src", "A.cs"),
            """
            public static class C
            {
            #if FLAG
                public static string Value => string.Empty;
            #endif
            }
            """,
            TestContext.Current.CancellationToken
        );
        await RestoreAsync(project);
        string[] arguments =
        [
            GetOutputPath("DrillPress.Cli"),
            "check",
            "--worker",
            "--build-host",
            GetOutputPath("DrillPress.BuildHost"),
            "--rules",
            GetOutputPath("DrillPress.SampleRules", "samples"),
            project,
        ];
        var first = await RunProcessAsync(
            "dotnet",
            arguments,
            directory.FullName,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory.FullName, "Directory.Build.props"),
            "<Project><PropertyGroup><DefineConstants>$(DefineConstants);FLAG</DefineConstants></PropertyGroup></Project>",
            TestContext.Current.CancellationToken
        );

        var imported = await RunProcessAsync(
            "dotnet",
            arguments,
            directory.FullName,
            TestContext.Current.CancellationToken
        );
        var stopped = await RunProcessAsync(
            "dotnet",
            [arguments[0], "stop-workers", .. arguments[2..]],
            directory.FullName,
            TestContext.Current.CancellationToken
        );

        Assert.Equal((0, "", ""), (first.ExitCode, first.StandardOutput, first.StandardError));
        Assert.Equal(
            (
                1,
                """
                DP1004 Use the empty string literal "" instead of string.Empty.
                src/A.cs
                  +4:35

                """.ReplaceLineEndings("\n"),
                ""
            ),
            (imported.ExitCode, imported.StandardOutput, imported.StandardError)
        );
        Assert.Equal(
            (0, "", ""),
            (stopped.ExitCode, stopped.StandardOutput, stopped.StandardError)
        );
    }

    private async Task WriteProjectAsync(string root, string name, string references, string source)
    {
        var directory = FileSystem.Path.Combine(root, name);
        FileSystem.Directory.CreateDirectory(directory);
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory, name + ".csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>{{references}}</ItemGroup>
            </Project>
            """,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory, name + ".cs"),
            source,
            TestContext.Current.CancellationToken
        );
    }

    private static string[] ReusedGenerations(string output) =>
        output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                using var json = JsonDocument.Parse(line["drillpress-profile ".Length..]);
                return json.RootElement.GetProperty("phase").GetString()!;
            })
            .Where(phase => phase.StartsWith("loading.reused.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public async Task Workers_repeat_complete_checks_and_invalidate_edited_project_sources()
    {
        var directory = CreateTemporaryDirectory("drillpress-worker-check-");
        var project = FileSystem.Path.Combine(directory.FullName, "Target.csproj");
        var source = FileSystem.Path.Combine(directory.FullName, "A.cs");
        await FileSystem.File.WriteAllTextAsync(
            project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            source,
            "public static class C { public static string Value => \"\"; }",
            TestContext.Current.CancellationToken
        );
        await RestoreAsync(project);
        string[] arguments =
        [
            GetOutputPath("DrillPress.Cli"),
            "check",
            "--worker",
            "--build-host",
            GetOutputPath("DrillPress.BuildHost"),
            "--rules",
            GetOutputPath("DrillPress.SampleRules", "samples"),
            project,
        ];

        var starting = await Task.WhenAll(
            RunProcessAsync(
                "dotnet",
                arguments,
                directory.FullName,
                TestContext.Current.CancellationToken
            ),
            RunProcessAsync(
                "dotnet",
                arguments,
                directory.FullName,
                TestContext.Current.CancellationToken
            )
        );
        var first = starting[0];
        var concurrent = starting[1];
        var repeated = await RunProcessAsync(
            "dotnet",
            arguments,
            directory.FullName,
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            source,
            "public static class C { public static string Value => string.Empty; }",
            TestContext.Current.CancellationToken
        );
        var edited = await RunProcessAsync(
            "dotnet",
            arguments,
            directory.FullName,
            TestContext.Current.CancellationToken
        );
        var fresh = await RunProcessAsync(
            "dotnet",
            arguments.Where(argument => argument != "--worker").ToArray(),
            directory.FullName,
            TestContext.Current.CancellationToken
        );
        var stopped = await RunProcessAsync(
            "dotnet",
            [arguments[0], "stop-workers", .. arguments[2..]],
            directory.FullName,
            TestContext.Current.CancellationToken
        );

        Assert.Equal((0, "", ""), (first.ExitCode, first.StandardOutput, first.StandardError));
        Assert.Equal(
            (0, "", ""),
            (concurrent.ExitCode, concurrent.StandardOutput, concurrent.StandardError)
        );
        Assert.Equal(
            (0, "", ""),
            (repeated.ExitCode, repeated.StandardOutput, repeated.StandardError)
        );
        Assert.Equal(1, edited.ExitCode);
        Assert.Equal("", edited.StandardError);
        Assert.Equal(
            """
            DP1004 Use the empty string literal "" instead of string.Empty.
            A.cs
              +1:55

            """.ReplaceLineEndings("\n"),
            edited.StandardOutput
        );
        Assert.Equal(
            (fresh.ExitCode, fresh.StandardOutput, fresh.StandardError),
            (edited.ExitCode, edited.StandardOutput, edited.StandardError)
        );
        Assert.Equal(
            (0, "", ""),
            (stopped.ExitCode, stopped.StandardOutput, stopped.StandardError)
        );
    }
}
