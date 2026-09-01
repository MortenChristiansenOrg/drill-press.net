using System.Diagnostics;
using System.Text.Json;
using DrillPress;
using DrillPress.SampleRules;
using Microsoft.CodeAnalysis;

var repositoryRoot = FindRepositoryRoot();
var sampleSolution = Path.Combine(repositoryRoot, "Sample Solution", "DrillPress.SampleTarget.slnx");
var ruleAssembly = typeof(SampleRuleSet).Assembly.Location;
var rules = SampleRuleSet.Create();
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
        ["run", "--project", buildHostProject, "--no-build", "--", "export", sampleSolution, manifestPath],
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
        ["run", "--project", buildHostProject, "--no-build", "--", "export", realisticSolution, realisticManifestPath],
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
        [ruleAssembly, "check", manifestPath],
        repositoryRoot);
    Check(check.ExitCode == 1, "A check with findings must exit with code 1.");
    var jsonLines = check.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    Check(jsonLines.Length == 8, "JSONL output must contain one line per finding.");
    var fixCount = 0;
    foreach (var line in jsonLines)
    {
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        Check(root.GetProperty("v").GetInt32() == 1, "Unexpected JSONL schema version.");
        Check(root.GetProperty("rule").GetString()!.StartsWith("DP", StringComparison.Ordinal),
            "JSONL rule id is missing.");
        Check(root.GetProperty("severity").GetString() == "warning", "JSONL severity is missing.");
        if (root.TryGetProperty("fixes", out _))
        {
            fixCount++;
        }
    }

    Check(fixCount == 3, "JSONL output must expose three fixable findings.");

    Console.WriteLine("conformance: automatic fixes");
    var copiedSolutionDirectory = Path.Combine(temporaryRoot, "Sample Solution");
    CopyDirectory(Path.Combine(repositoryRoot, "Sample Solution"), copiedSolutionDirectory);
    var copiedSolution = Path.Combine(copiedSolutionDirectory, "DrillPress.SampleTarget.slnx");
    var fix = await RunAsync(
        "dotnet",
        [ruleAssembly, "fix", copiedSolution],
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
            var actualErrors = project.Compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Check(actualErrors.Length == expectedErrors,
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

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
