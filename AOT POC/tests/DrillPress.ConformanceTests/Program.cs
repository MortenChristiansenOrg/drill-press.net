using System.Diagnostics;
using System.Text.Json;
using DrillPress;
using DrillPress.SampleRules;
using Microsoft.CodeAnalysis;

var repositoryRoot = FindRepositoryRoot();
var sampleSolution = Path.Combine(repositoryRoot, "Sample Solution", "DrillPress.SampleTarget.slnx");
var ruleAssembly = typeof(SampleRuleSet).Assembly.Location;
var rules = SampleRuleSet.Create();
var buildConfiguration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
    ?? throw new InvalidOperationException("Could not determine the test build configuration.");
var temporaryRoot = Path.Combine(Path.GetTempPath(), $"drillpress-conformance-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryRoot);

try
{
    Console.WriteLine("conformance: direct target modes");
    var directDiagnostics = await EvaluateAsync(sampleSolution, 8);
    await EvaluateAsync(Path.GetDirectoryName(sampleSolution)!, 8);
    await EvaluateAsync(
        Path.Combine(repositoryRoot, "Sample Solution", "src", "WidgetLibrary", "WidgetLibrary.csproj"),
        5);
    await EvaluateAsync(
        Path.Combine(repositoryRoot, "Sample Solution", "src", "WidgetLibrary", "WidgetService.cs"),
        3);
    await EvaluateAsync(
        Path.Combine(repositoryRoot, "Sample Solution", "src", "**", "*.cs"),
        5);

    Check(directDiagnostics.Count(diagnostic => diagnostic.Descriptor.Id == "DP1002") == 1,
        "The assertion-ordering rule must have exactly one violation.");
    Check(!directDiagnostics.Any(diagnostic =>
            diagnostic.Descriptor.Id == "DP1002" &&
            diagnostic.Location.Document.Text.Lines.GetLinePosition(diagnostic.Location.Span.Start).Line + 1 == 35),
        "A sole Assert.Throws before the second empty line must be exempt from DP1002.");
    Check(directDiagnostics.SelectMany(static diagnostic => diagnostic.Fixes).Count() == 3,
        "The sample must expose exactly three safe edits.");

    Console.WriteLine("conformance: SDK manifest export and reconstruction");
    var manifestPath = Path.Combine(temporaryRoot, "sample.drillpress.json");
    var buildHostProject = Path.Combine(
        repositoryRoot,
        "AOT POC",
        "src",
        "DrillPress.BuildHost",
        "DrillPress.BuildHost.csproj");
    var export = await RunAsync(
        "dotnet",
        [
            "run",
            "--project",
            buildHostProject,
            "--configuration",
            buildConfiguration,
            "--no-build",
            "--",
            "export",
            sampleSolution,
            manifestPath,
        ],
        repositoryRoot);
    Check(export.ExitCode == 0, $"Manifest export failed:{Environment.NewLine}{export.StandardError}");

    await using (var manifestStream = File.OpenRead(manifestPath))
    {
        var manifest = await JsonSerializer.DeserializeAsync(
                manifestStream,
                CompilationManifestJsonContext.Default.CompilationManifest)
            ?? throw new InvalidOperationException("Manifest deserialized to null.");
        Check(manifest.Version == CompilationManifest.CurrentVersion, "Manifest version mismatch.");
        Check(manifest.Projects.Length == 2, "The sample manifest must contain two projects.");
        Check(manifest.Projects.All(static project => project.CompilerErrorCount == 0),
            "Build-host compilations must be free of compiler errors.");
        Check(manifest.Projects.All(static project => project.MetadataReferences.Length > 100),
            "Evaluated framework references were not captured.");
        Check(manifest.Projects.Sum(static project => project.Documents.Count(static document => document.IsGenerated)) >= 4,
            "SDK-generated source documents were not captured.");
    }

    var manifestSolution = await SolutionLoader.LoadAsync(manifestPath);
    foreach (var project in manifestSolution.Projects)
    {
        var errors = project.Compilation.GetDiagnostics().Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Check(errors == 0, $"Reconstructed project '{project.Name}' has {errors} compiler error(s).");
    }

    var manifestDiagnostics = rules.Evaluate(manifestSolution);
    Check(Signatures(manifestDiagnostics).SequenceEqual(Signatures(directDiagnostics), StringComparer.Ordinal),
        "Manifest and direct loading produced different rule diagnostics.");

    var fastManifestPath = Path.Combine(temporaryRoot, "sample-fast.drillpress.json");
    var fastExport = await RunAsync(
        "dotnet",
        [
            "run",
            "--project",
            buildHostProject,
            "--configuration",
            buildConfiguration,
            "--no-build",
            "--",
            "export",
            sampleSolution,
            fastManifestPath,
            "--skip-compiler-diagnostics",
        ],
        repositoryRoot);
    Check(fastExport.ExitCode == 0, $"Fast manifest export failed:{Environment.NewLine}{fastExport.StandardError}");
    var fastManifest = await ReadManifestAsync(fastManifestPath);
    Check(fastManifest.Projects.All(static project => project.CompilerErrorCount is null),
        "Fast manifests must mark compiler diagnostics as unevaluated.");
    var fastManifestDiagnostics = rules.Evaluate(await SolutionLoader.LoadAsync(fastManifestPath));
    Check(Signatures(fastManifestDiagnostics).SequenceEqual(Signatures(manifestDiagnostics), StringComparer.Ordinal),
        "Fast and validated manifests produced different rule diagnostics.");

    Console.WriteLine("conformance: evaluated test classification and diagnostic aggregation");
    var classificationSolution = Path.Combine(
        repositoryRoot,
        "AOT POC",
        "tests",
        "Fixtures",
        "ClassificationSolution",
        "ClassificationFixture.slnx");
    var classificationManifestPath = Path.Combine(temporaryRoot, "classification.drillpress.json");
    var classificationExport = await RunAsync(
        "dotnet",
        [
            "run",
            "--project",
            buildHostProject,
            "--configuration",
            buildConfiguration,
            "--no-build",
            "--",
            "export",
            classificationSolution,
            classificationManifestPath,
            "--skip-compiler-diagnostics",
        ],
        repositoryRoot);
    Check(classificationExport.ExitCode == 0,
        $"Classification manifest export failed:{Environment.NewLine}{classificationExport.StandardError}");
    var classificationManifest = await ReadManifestAsync(classificationManifestPath);
    Check(!classificationManifest.Projects.Single(project => project.Name == "Misleading.Tests").IsTestProject,
        "An explicit evaluated IsTestProject=false must override test-like names and paths.");
    Check(classificationManifest.Projects.Single(project => project.Name == "Odd.Specifications").IsTestProject,
        "An explicit evaluated IsTestProject=true must override the naming heuristic.");
    var classificationSolutionModel = await SolutionLoader.LoadAsync(classificationManifestPath);
    var classificationDiagnostics = rules.Evaluate(classificationSolutionModel);
    Check(classificationDiagnostics.Count(diagnostic => diagnostic.Descriptor.Id == "DP1004") == 2,
        "The linked source must be evaluated independently in both project contexts.");

    var aggregatedJson = await RunAsync(
        "dotnet",
        [
            ruleAssembly,
            "check",
            classificationManifestPath,
            "--format",
            "jsonl",
            "--details",
            "--include-contexts",
        ],
        repositoryRoot);
    Check(aggregatedJson.ExitCode == 1, "The aggregated linked-source check must report a finding.");
    var aggregatedLines = aggregatedJson.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    Check(aggregatedLines.Length == 1,
        "Identical linked-source findings must collapse to one source diagnostic.");
    using (var aggregatedDocument = JsonDocument.Parse(aggregatedLines[0]))
    {
        var root = aggregatedDocument.RootElement;
        Check(root.GetProperty("contexts").GetArrayLength() == 2,
            "Opt-in output must retain both contributing project contexts.");
        Check(root.GetProperty("fixes").GetArrayLength() == 1,
            "A fix that is safe in every context must remain available once.");
        Check(!root.GetProperty("fixes")[0].TryGetProperty("file", out _),
            "A same-file fix must not repeat the diagnostic file path.");
    }

    Console.WriteLine("conformance: realistic evaluated project features");
    var realisticSolution = Path.Combine(
        repositoryRoot,
        "AOT POC",
        "tests",
        "Fixtures",
        "RealisticSolution",
        "RealisticFixture.slnx");
    var realisticManifestPath = Path.Combine(temporaryRoot, "realistic.drillpress.json");
    var realisticBuild = await RunAsync("dotnet", ["build", realisticSolution, "--nologo"], repositoryRoot);
    Check(realisticBuild.ExitCode == 0,
        $"Realistic fixture build failed:{Environment.NewLine}{realisticBuild.StandardOutput}");
    var realisticExport = await RunAsync(
        "dotnet",
        [
            "run",
            "--project",
            buildHostProject,
            "--configuration",
            buildConfiguration,
            "--no-build",
            "--",
            "export",
            realisticSolution,
            realisticManifestPath,
        ],
        repositoryRoot);
    Check(realisticExport.ExitCode == 0,
        $"Realistic manifest export failed:{Environment.NewLine}{realisticExport.StandardError}");
    var realisticManifest = await ReadManifestAsync(realisticManifestPath);
    Check(realisticManifest.Projects.Length == 3, "The realistic manifest must contain three projects.");
    var libraryManifest = realisticManifest.Projects.Single(project => project.Name == "Fixture.Library");
    Check(libraryManifest.PreprocessorSymbols.Contains("DRILLPRESS_FEATURE", StringComparer.Ordinal),
        "Conditional MSBuild symbols were not captured.");
    Check(libraryManifest.MetadataReferences.Any(reference =>
            Path.GetFileName(reference).Equals("Humanizer.dll", StringComparison.OrdinalIgnoreCase)),
        "The NuGet compile reference was not captured.");
    Check(libraryManifest.Documents.Any(document =>
            document.Path.EndsWith("LinkedContract.cs", StringComparison.Ordinal) && !document.IsGenerated),
        "Linked source was not captured as an ordinary document.");
    Check(libraryManifest.Documents.Any(document =>
            document.Path.EndsWith("GeneratedMarker.g.cs", StringComparison.Ordinal) &&
            document.IsGenerated &&
            document.Text.Contains("generated-from-additional-file", StringComparison.Ordinal)),
        "Source-generator output was not captured.");
    Check(libraryManifest.AdditionalDocuments.Any(document =>
            document.Path.EndsWith("GeneratorInput.txt", StringComparison.Ordinal)),
        "AdditionalFiles were not captured.");
    Check(libraryManifest.AnalyzerConfigDocuments.Any(document =>
            document.Path.EndsWith(".editorconfig", StringComparison.Ordinal)),
        "Analyzer configuration was not captured.");
    Check(libraryManifest.Documents.Any(document =>
            document.CompilerDiagnosticOptions.TryGetValue("CS9113", out var severity) &&
            severity == (int)ReportDiagnostic.Suppress),
        "Effective per-tree compiler severity was not captured.");
    Check(realisticManifest.Projects.Single(project => project.Name == "Fixture.Library.Tests").IsTestProject,
        "Evaluated test-project classification was not preserved.");
    var realisticReconstruction = await SolutionLoader.LoadAsync(realisticManifestPath);
    foreach (var project in realisticReconstruction.Projects)
    {
        var errors = project.Compilation.GetDiagnostics().Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Check(errors == 0, $"Realistic reconstructed project '{project.Name}' has {errors} compiler error(s).");
    }

    var generatedDiagnostic = rules.Evaluate(realisticReconstruction).Single(diagnostic =>
        diagnostic.Descriptor.Id == "DP1004" && diagnostic.Location.Document.IsGenerated);
    Check(generatedDiagnostic.Fixes.IsEmpty,
        "Diagnostics in generated source must not offer edits to generated output.");

    Console.WriteLine("conformance: JSONL process contract");
    var check = await RunAsync(
        "dotnet",
        [ruleAssembly, "check", manifestPath, "--format", "jsonl", "--details"],
        repositoryRoot);
    Check(check.ExitCode == 1, "A check with findings must exit with code 1.");
    var jsonLines = check.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    Check(jsonLines.Length == 8, "JSONL output must contain one line per finding.");
    var fixCount = 0;
    foreach (var line in jsonLines)
    {
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        Check(root.GetProperty("rule").GetString()!.StartsWith("DP", StringComparison.Ordinal),
            "JSONL rule id is missing.");
        Check(!root.TryGetProperty("v", out _) && !root.TryGetProperty("severity", out _),
            "JSONL must not repeat constant schema or severity properties.");
        Check(root.TryGetProperty("start", out _) && root.TryGetProperty("length", out _),
            "Detailed JSONL must include exact diagnostic spans.");
        if (root.TryGetProperty("fixes", out _))
        {
            fixCount++;
        }
    }

    Check(fixCount == 3, "JSONL output must expose three fixable findings.");

    var minimalJson = await RunAsync(
        "dotnet",
        [ruleAssembly, "check", manifestPath, "--format", "jsonl"],
        repositoryRoot);
    Check(minimalJson.ExitCode == 1, "Minimal JSONL must preserve the findings exit code.");
    foreach (var line in minimalJson.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        Check(!root.TryGetProperty("start", out _) &&
              !root.TryGetProperty("length", out _) &&
              !root.TryGetProperty("fixes", out _) &&
              !root.TryGetProperty("contexts", out _),
            "Minimal JSONL included opt-in details.");
    }

    var llmCheck = await RunAsync("dotnet", [ruleAssembly, "check", manifestPath], repositoryRoot);
    Check(llmCheck.ExitCode == 1, "Default LLM output must preserve the findings exit code.");
    Check(llmCheck.StandardOutput.Length < minimalJson.StandardOutput.Length,
        "Grouped LLM output must be smaller than minimal JSONL.");
    Check(CountOccurrences(llmCheck.StandardOutput, "  +") == 3,
        "Default LLM output must mark exactly three common-safe fixes.");
    foreach (var ruleId in directDiagnostics.Select(static diagnostic => diagnostic.Descriptor.Id).Distinct())
    {
        Check(CountOccurrences(llmCheck.StandardOutput, ruleId) == 1,
            $"Default LLM output repeated rule guidance for {ruleId}.");
    }

    var profiledCheck = await RunAsync(
        "dotnet",
        [ruleAssembly, "check", manifestPath, "--profile"],
        repositoryRoot);
    Check(profiledCheck.ExitCode == 1, "A profiled check must preserve the findings exit code.");
    Check(profiledCheck.StandardError.Contains("drillpress: phases:", StringComparison.Ordinal) &&
          profiledCheck.StandardError.Contains("drillpress: rule-phase:", StringComparison.Ordinal),
        "Profiled checks must report load and rule phase timing on stderr.");

    Console.WriteLine("conformance: CLI BuildHost orchestration");
    var cliAssembly = Path.Combine(
        repositoryRoot,
        "AOT POC",
        "src",
        "DrillPress.Cli",
        "bin",
        buildConfiguration,
        "net10.0",
        "drillpress.dll");
    var buildHostAssembly = Path.Combine(
        repositoryRoot,
        "AOT POC",
        "src",
        "DrillPress.BuildHost",
        "bin",
        buildConfiguration,
        "net10.0",
        "DrillPress.BuildHost.dll");
    var coordinatedCheck = await RunAsync(
        "dotnet",
        [
            cliAssembly,
            "check",
            "--rules",
            ruleAssembly,
            sampleSolution,
            "--build-host",
            buildHostAssembly,
            "--fast",
            "--format",
            "jsonl",
        ],
        repositoryRoot);
    Check(coordinatedCheck.ExitCode == 1, "The coordinated check must preserve the findings exit code.");
    Check(coordinatedCheck.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 8,
        "The coordinator must export and analyze all eight sample findings.");
    Check(coordinatedCheck.StandardError.Contains("compiler diagnostics skipped", StringComparison.Ordinal),
        "The coordinator did not forward fast export mode to BuildHost.");

    var coordinatedSolutionDirectory = Path.Combine(temporaryRoot, "Coordinated Sample Solution");
    CopyDirectory(Path.Combine(repositoryRoot, "Sample Solution"), coordinatedSolutionDirectory);
    var coordinatedSolution = Path.Combine(coordinatedSolutionDirectory, "DrillPress.SampleTarget.slnx");
    var coordinatedFix = await RunAsync(
        "dotnet",
        [
            cliAssembly,
            "fix",
            "--rules",
            ruleAssembly,
            coordinatedSolution,
            "--build-host",
            buildHostAssembly,
            "--fast",
            "--format",
            "jsonl",
        ],
        repositoryRoot);
    Check(coordinatedFix.ExitCode == 1,
        "The coordinated fix must report remaining unfixable findings with exit code 1.");
    Check(coordinatedFix.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 5,
        "The coordinated fix must regenerate its manifest and output five remaining findings.");
    Check(coordinatedFix.StandardError.Contains("applied 3 edit(s)", StringComparison.Ordinal),
        "The coordinated fix did not report its three deduplicated edits.");
    var coordinatedSources = Directory.EnumerateFiles(coordinatedSolutionDirectory, "*.cs", SearchOption.AllDirectories)
        .Select(File.ReadAllText)
        .ToArray();
    Check(!coordinatedSources.Any(source => source.Contains("string.Empty", StringComparison.Ordinal)),
        "string.Empty remained after the coordinated fix.");
    Check(!coordinatedSources.Any(source => source.Contains("StringComparer.Ordinal", StringComparison.Ordinal)),
        "StringComparer.Ordinal remained after the coordinated fix.");
    var coordinatedBuild = await RunAsync("dotnet", ["build", coordinatedSolution, "--nologo"], repositoryRoot);
    Check(coordinatedBuild.ExitCode == 0,
        $"The solution fixed through the coordinator does not build:{Environment.NewLine}" +
        coordinatedBuild.StandardOutput);

    Console.WriteLine("conformance: automatic fixes");
    var copiedSolutionDirectory = Path.Combine(temporaryRoot, "Sample Solution");
    CopyDirectory(Path.Combine(repositoryRoot, "Sample Solution"), copiedSolutionDirectory);
    var copiedSolution = Path.Combine(copiedSolutionDirectory, "DrillPress.SampleTarget.slnx");
    var fix = await RunAsync(
        "dotnet",
        [ruleAssembly, "fix", copiedSolution, "--format", "jsonl"],
        repositoryRoot);
    Check(fix.ExitCode == 1, "Fix must report remaining unfixable findings with exit code 1.");
    Check(fix.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 5,
        "Fix must re-analyze and output five remaining findings.");
    var copiedSources = Directory.EnumerateFiles(copiedSolutionDirectory, "*.cs", SearchOption.AllDirectories)
        .Select(File.ReadAllText)
        .ToArray();
    Check(!copiedSources.Any(source => source.Contains("string.Empty", StringComparison.Ordinal)),
        "string.Empty remained after applying fixes.");
    Check(!copiedSources.Any(source => source.Contains("StringComparer.Ordinal", StringComparison.Ordinal)),
        "StringComparer.Ordinal remained after applying fixes.");
    var build = await RunAsync("dotnet", ["build", copiedSolution, "--nologo"], repositoryRoot);
    Check(build.ExitCode == 0, $"The fixed solution does not build:{Environment.NewLine}{build.StandardOutput}");

    if (args is ["--manifest", var externalManifestPath])
    {
        Console.WriteLine("conformance: external manifest reconstruction");
        var externalManifest = await ReadManifestAsync(Path.GetFullPath(externalManifestPath));
        var externalSolution = await SolutionLoader.LoadAsync(Path.GetFullPath(externalManifestPath));
        foreach (var project in externalSolution.Projects)
        {
            var expectedErrors = externalManifest.Projects
                .Single(candidate => candidate.ProjectPath == project.Path && candidate.Name == project.Name)
                .CompilerErrorCount;
            if (expectedErrors is null)
            {
                continue;
            }

            var actualErrors = project.Compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Check(actualErrors.Length == expectedErrors.Value,
                $"Reconstructed project '{project.Name}' has {actualErrors.Length} compiler error(s); " +
                $"the build-host compilation had {expectedErrors}:{Environment.NewLine}" +
                string.Join(Environment.NewLine, actualErrors.Take(10)));
        }
    }
    else if (args.Length != 0)
    {
        throw new InvalidOperationException("Usage: DrillPress.ConformanceTests [--manifest <path>]");
    }

    Console.WriteLine("conformance: PASS");
    return 0;
}
finally
{
    if (Directory.Exists(temporaryRoot))
    {
        Directory.Delete(temporaryRoot, recursive: true);
    }
}

async Task<IReadOnlyList<RuleDiagnostic>> EvaluateAsync(string target, int expectedCount)
{
    var solution = await SolutionLoader.LoadAsync(target);
    var diagnostics = rules.Evaluate(solution);
    Check(diagnostics.Length == expectedCount,
        $"Expected {expectedCount} diagnostics for '{target}', found {diagnostics.Length}.");
    return diagnostics;
}

static IEnumerable<string> Signatures(IEnumerable<RuleDiagnostic> diagnostics) => diagnostics.Select(diagnostic =>
    $"{diagnostic.Descriptor.Id}|{Path.GetFileName(diagnostic.Location.Document.Path)}|" +
    $"{diagnostic.Location.Span.Start}|{diagnostic.Location.Span.Length}");

static async Task<CompilationManifest> ReadManifestAsync(string path)
{
    await using var stream = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync(
               stream,
               CompilationManifestJsonContext.Default.CompilationManifest)
           ?? throw new InvalidOperationException($"Manifest '{path}' deserialized to null.");
}

static async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
{
    var startInfo = new ProcessStartInfo(fileName)
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
    var standardOutput = process.StandardOutput.ReadToEndAsync();
    var standardError = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
}

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.EnumerateFiles(source))
    {
        File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    }

    foreach (var directory in Directory.EnumerateDirectories(source))
    {
        var name = Path.GetFileName(directory);
        if (name is "bin" or "obj")
        {
            continue;
        }

        CopyDirectory(directory, Path.Combine(destination, name));
    }
}

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
            Directory.Exists(Path.Combine(directory.FullName, "AOT POC")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("Could not locate the repository root.");
}

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static int CountOccurrences(string value, string search)
{
    var count = 0;
    for (var index = 0; (index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0; index += search.Length)
    {
        count++;
    }

    return count;
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
