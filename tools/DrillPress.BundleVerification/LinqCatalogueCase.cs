using System.IO.Abstractions;
using System.Text;

namespace DrillPress.BundleVerification;

public sealed class LinqCatalogueCase(
    IFileSystem fileSystem,
    string repository,
    string fixture,
    string output,
    string runtimeIdentifier
)
{
    public async Task VerifyAsync()
    {
        fileSystem.Directory.CreateDirectory(fixture);
        var project = fileSystem.Path.Combine(fixture, "LinqProbe.csproj");
        await fileSystem.File.WriteAllTextAsync(
            project,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
                <EnableTrimAnalyzer>true</EnableTrimAnalyzer><EnableAotAnalyzer>true</EnableAotAnalyzer>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{fileSystem.Path.Combine(
                repository,
                "src/DrillPress.Linq/DrillPress.Linq.csproj"
            )}}" />
                <ProjectReference Include="{{fileSystem.Path.Combine(
                repository,
                "src/DrillPress.Testing/DrillPress.Testing.csproj"
            )}}" />
              </ItemGroup>
            </Project>
            """
        );
        await fileSystem.File.WriteAllTextAsync(
            fileSystem.Path.Combine(fixture, "Program.cs"),
            """"
            using System.IO.Abstractions;
            using DrillPress;
            using DrillPress.Presets;
            using DrillPress.Testing;
            using Microsoft.CodeAnalysis;

            var fileSystem = new FileSystem();
            var references = fileSystem.File.ReadAllLines(args[0])
                .Select(path => MetadataReference.CreateFromImage(fileSystem.File.ReadAllBytes(path))).ToArray();
            var workspace = new RuleTestWorkspace(references);
            workspace.AddProject("Probe", [new("Probe.cs", """
                using System.Linq;
                class C { void M(int[] items, IQueryable<int> query) {
                    _ = items.SequenceEqual(query); _ = Enumerable.ToList(query);
                    _ = query.Count(); _ = items.Where(x => x > 0); _ = items.AsEnumerable();
                } }
                """)]);
            foreach (var call in Code.Calls.In(workspace.Analyze())) {
                var operation = StandardLinq.Inspect(call);
                Console.WriteLine($"{operation.Status} {operation.Surface} {operation.Category}: {string.Join(",", operation.SequenceInputs.Select(input => input.Role + ":" + input.Value.Syntax))}");
            }
            """"
        );
        var inventory = fileSystem.Path.Combine(fixture, "references.txt");
        await fileSystem.File.WriteAllLinesAsync(
            inventory,
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
                fileSystem.Path.PathSeparator
            )
        );
        const string expected = """
            Supported Enumerable Scalar: first:items,second:query
            Supported Enumerable Materializer: source:query
            Supported Queryable Scalar: source:query
            Supported Enumerable DeferredConstruction: source:items
            Supported Enumerable Adapter: source:items
            """;
        foreach (var mode in Enum.GetValues<BundleMode>())
        {
            var destination = fileSystem.Path.Combine(output, "linq-" + mode);
            var publication = await ProcessRunner.RunAsync(
                "dotnet",
                [
                    "publish",
                    project,
                    "-c",
                    "Release",
                    "-r",
                    runtimeIdentifier,
                    "-p:PublishAot=" + (mode == BundleMode.Native ? "true" : "false"),
                    "--self-contained",
                    mode == BundleMode.Native ? "true" : "false",
                    "-o",
                    destination,
                ],
                repository,
                timeout: TimeSpan.FromMinutes(10)
            );
            await fileSystem.File.WriteAllBytesAsync(
                fileSystem.Path.Combine(output, $"linq-{mode}-publish.log"),
                [.. publication.StandardOutput, .. publication.StandardError]
            );
            ProcessRunner.RequireSuccess(publication);
            PublishWarnings.Validate(
                Encoding.UTF8.GetString(publication.StandardOutput)
                    + Encoding.UTF8.GetString(publication.StandardError)
            );
            var executable = fileSystem.Path.Combine(
                destination,
                "LinqProbe"
                    + (
                        mode == BundleMode.Managed ? ".dll"
                        : OperatingSystem.IsWindows() ? ".exe"
                        : ""
                    )
            );
            var result = await ProcessRunner.RunAsync(
                mode == BundleMode.Managed ? "dotnet" : executable,
                mode == BundleMode.Managed ? [executable, inventory] : [inventory],
                repository
            );
            await fileSystem.File.WriteAllBytesAsync(
                fileSystem.Path.Combine(output, $"linq-{mode}.stdout"),
                result.StandardOutput
            );
            ProcessRunner.RequireSuccess(result);
            if (
                Encoding.UTF8.GetString(result.StandardOutput).Replace("\r\n", "\n")
                    != expected + "\n"
                || result.StandardError.Length != 0
            )
                throw new InvalidOperationException($"The {mode} LINQ catalogue contract differs.");
        }
        Console.WriteLine(
            "LINQ catalogue: managed/native exact classifications and input roles match"
        );
    }
}
