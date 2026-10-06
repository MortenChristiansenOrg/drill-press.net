using System.IO.Abstractions;
using System.Text;
using System.Xml.Linq;

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
        var linqReference = new XAttribute(
            "Include",
            fileSystem.Path.Combine(repository, "src/DrillPress.Linq/DrillPress.Linq.csproj")
        );
        var testingReference = new XAttribute(
            "Include",
            fileSystem.Path.Combine(repository, "src/DrillPress.Testing/DrillPress.Testing.csproj")
        );
        await fileSystem.File.WriteAllTextAsync(
            project,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
                <TrimmerSingleWarn>false</TrimmerSingleWarn>
                <WarningsNotAsErrors>IL2091;IL3000</WarningsNotAsErrors>
                <EnableTrimAnalyzer>true</EnableTrimAnalyzer><EnableAotAnalyzer>true</EnableAotAnalyzer>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference {{linqReference}} />
                <ProjectReference {{testingReference}} />
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
            using RuleSet = DrillPress.RuleSet;

            var fileSystem = new FileSystem();
            var references = fileSystem.File.ReadAllLines(args[0])
                .Select(path => MetadataReference.CreateFromImage(fileSystem.File.ReadAllBytes(path))).ToArray();
            var workspace = new RuleTestWorkspace(references);
            workspace.AddProject("Probe", [new("Probe.cs", """
                using System.Linq;
                class C { void M(int[] items, IQueryable<int> query) {
                    _ = items.SequenceEqual(query); _ = Enumerable.ToList(query);
                    _ = query.Count(); _ = items.Where(x => x > 0); _ = items.AsEnumerable();
                    _ = items.Reverse();
                    _ = items.TryGetNonEnumeratedCount(out var first);
                    _ = Enumerable.TryGetNonEnumeratedCount(items, out var second);
                    _ = items.Count();
                    foreach (var item in items) { }
                } }
                """)]);
            var solution = workspace.Analyze();
            foreach (var call in Code.Calls.In(solution)) {
                var operation = StandardLinq.Inspect(call);
                Console.WriteLine($"{operation.Status} {operation.Surface} {operation.Category} {operation.SequenceConsumption}: {string.Join(",", operation.SequenceInputs.Select(input => input.Role + ":" + input.Value.Syntax))}");
            }
            var rules = new RuleSet();
            var policy = rules.Rule("LINQ", "Review consuming operations.");
            policy.For(Code.Calls.Where(call => StandardLinq.Inspect(call).SequenceConsumption == LinqSequenceConsumption.MayEnumerate).Expressions())
                .Require(Coverage.Executed);
            policy.For(Code.Calls.Where(call => StandardLinq.Inspect(call) is { Surface: LinqSurface.Queryable, Category: LinqOperationCategory.Scalar }))
                .Require(Coverage.Executed);
            policy.For(Code.Enumerations).Require(Coverage.EnumerationStarted);
            Console.WriteLine("Shared rule: " + string.Join(",", rules.Evaluate(solution).Select(diagnostic =>
                diagnostic.Descriptor.Id + ":" + diagnostic.Source!.Document.Text.Substring(diagnostic.Location.Start, diagnostic.Location.Length)
                    + ":" + string.Join("/", diagnostic.Coverage.Select(evidence => evidence.Metric + "=" + evidence.State)))));
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
            Supported Enumerable Scalar MayEnumerate: first:items,second:query
            Supported Enumerable Materializer MayEnumerate: source:query
            Supported Queryable Scalar Unknown: source:query
            Supported Enumerable DeferredConstruction Unknown: source:items
            Supported Enumerable Adapter Unknown: source:items
            Supported Enumerable DeferredConstruction Unknown: source:items
            Supported Enumerable Scalar NeverEnumerates: source:items
            Supported Enumerable Scalar NeverEnumerates: source:items
            Supported Enumerable Scalar MayEnumerate: source:items
            Shared rule: LINQ:items.SequenceEqual(query):Execution=Unknown,LINQ:Enumerable.ToList(query):Execution=Unknown,LINQ:query.Count():Execution=Unknown,LINQ:items.Count():Execution=Unknown,LINQ:items:Enumeration=Unknown
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
                    != expected.Replace("\r\n", "\n") + "\n"
                || result.StandardError.Length != 0
            )
                throw new InvalidOperationException($"The {mode} LINQ catalogue contract differs.");
        }
        Console.WriteLine(
            "LINQ catalogue: managed/native classifications, consumption facts and shared clauses match"
        );
    }
}
