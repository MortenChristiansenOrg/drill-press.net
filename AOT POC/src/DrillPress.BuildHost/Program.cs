using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using DrillPress;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

if (!TryParseArguments(
        args,
        out var target,
        out var output,
        out var properties,
        out var allowCompilerErrors,
        out var skipCompilerDiagnostics))
{
    Console.Error.WriteLine(
        "Usage: DrillPress.BuildHost export <solution|project|directory> <manifest.json> " +
        "[--property Name=Value] [--allow-compiler-errors] [--skip-compiler-diagnostics]");
    return 2;
}

try
{
    var stopwatch = Stopwatch.StartNew();
    target = ResolveTarget(target);
    MSBuildLocator.RegisterDefaults();

    using var workspace = MSBuildWorkspace.Create(properties);
    var workspaceMessages = ImmutableArray.CreateBuilder<ManifestMessage>();
    var workspaceMessagesLock = new object();
    workspace.RegisterWorkspaceFailedHandler(eventArgs =>
    {
        lock (workspaceMessagesLock)
        {
            workspaceMessages.Add(new ManifestMessage(
                eventArgs.Diagnostic.Kind.ToString(),
                eventArgs.Diagnostic.Message));
        }
    });

    var openStarted = Stopwatch.GetTimestamp();
    var solution = Path.GetExtension(target).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
        ? (await workspace.OpenProjectAsync(target)).Solution
        : await workspace.OpenSolutionAsync(target);
    var openElapsed = Stopwatch.GetElapsedTime(openStarted);
    var csharpProjects = solution.Projects
        .Where(static project => project.Language == LanguageNames.CSharp)
        .OrderBy(static project => project.FilePath, StringComparer.Ordinal)
        .ToImmutableArray();
    var compilationStarted = Stopwatch.GetTimestamp();
    var projectCompilations = await Task.WhenAll(
        csharpProjects.Select(static project => project.GetCompilationAsync()));
    var compilationElapsed = Stopwatch.GetElapsedTime(compilationStarted);
    var diagnosticsElapsed = TimeSpan.Zero;
    var projects = ImmutableArray.CreateBuilder<ManifestProject>();
    var totalCompilerErrors = 0;
    var generatorElapsed = TimeSpan.Zero;
    var documentElapsed = TimeSpan.Zero;

    for (var projectIndex = 0; projectIndex < csharpProjects.Length; projectIndex++)
    {
        var project = csharpProjects[projectIndex];
        var projectCompilation = projectCompilations[projectIndex];
        if (projectCompilation is not CSharpCompilation compilation ||
            project.ParseOptions is not CSharpParseOptions parseOptions ||
            project.CompilationOptions is not CSharpCompilationOptions compilationOptions)
        {
            lock (workspaceMessagesLock)
            {
                workspaceMessages.Add(new ManifestMessage(
                    "Failure",
                    $"Could not create a C# compilation for '{project.Name}'.",
                    project.FilePath));
            }

            continue;
        }

        // Force generators to complete before reading Compilation.SyntaxTrees.
        var generatorStarted = Stopwatch.GetTimestamp();
        _ = await project.GetSourceGeneratedDocumentsAsync();
        generatorElapsed += Stopwatch.GetElapsedTime(generatorStarted);
        var documentStarted = Stopwatch.GetTimestamp();
        var ordinaryPaths = project.Documents
            .Select(static document => document.FilePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path!))
            .ToHashSet(GetPathComparer());
        var additionalDocuments = await CaptureTextDocumentsAsync(project.AdditionalDocuments, project.Name);
        var analyzerConfigDocuments = await CaptureTextDocumentsAsync(project.AnalyzerConfigDocuments, project.Name);
        var compilerDiagnosticIds = GetConfiguredCompilerDiagnosticIds(analyzerConfigDocuments);
        var globalCompilerDiagnosticOptions = CaptureGlobalCompilerDiagnosticOptions(
            compilationOptions.SyntaxTreeOptionsProvider,
            compilerDiagnosticIds);
        var documents = compilation.SyntaxTrees
            .Select((tree, index) =>
            {
                var path = string.IsNullOrWhiteSpace(tree.FilePath)
                    ? $"drillpress-generated://{project.Name}/{index:D6}.g.cs"
                    : Path.GetFullPath(tree.FilePath);
                var generated = !ordinaryPaths.Contains(path) || IsUnderBuildOutput(path, project.FilePath);
                return new ManifestDocument(
                    path,
                    tree.GetText().ToString(),
                    generated,
                    CaptureCompilerDiagnosticOptions(
                        compilationOptions.SyntaxTreeOptionsProvider,
                        tree,
                        compilerDiagnosticIds));
            })
            .OrderBy(static document => document.Path, StringComparer.Ordinal)
            .ToImmutableArray();
        documentElapsed += Stopwatch.GetElapsedTime(documentStarted);
        var metadataReferences = compilation.References
            .OfType<PortableExecutableReference>()
            .Select(static reference => reference.FilePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(static path => Path.GetFullPath(path!))
            .Distinct(GetPathComparer())
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToImmutableArray();
        var compilerErrors = ImmutableArray<Diagnostic>.Empty;
        int? compilerErrorCount = null;
        if (!skipCompilerDiagnostics)
        {
            var diagnosticsStarted = Stopwatch.GetTimestamp();
            compilerErrors = compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
            diagnosticsElapsed += Stopwatch.GetElapsedTime(diagnosticsStarted);
            compilerErrorCount = compilerErrors.Length;
            totalCompilerErrors += compilerErrorCount.Value;
        }
        var specificDiagnostics = compilationOptions.SpecificDiagnosticOptions
            .ToImmutableDictionary(
                static pair => pair.Key,
                static pair => (int)pair.Value,
                StringComparer.Ordinal);

        projects.Add(new ManifestProject(
            project.Id.Id.ToString("D"),
            project.Name,
            project.AssemblyName ?? project.Name,
            Path.GetFullPath(project.FilePath ?? throw new InvalidOperationException($"Project '{project.Name}' has no path.")),
            IsTestProject(project),
            (int)parseOptions.LanguageVersion,
            (int)parseOptions.DocumentationMode,
            (int)parseOptions.Kind,
            parseOptions.PreprocessorSymbolNames.ToImmutableArray(),
            (int)compilationOptions.OutputKind,
            (int)compilationOptions.NullableContextOptions,
            (int)compilationOptions.OptimizationLevel,
            (int)compilationOptions.Platform,
            (int)compilationOptions.GeneralDiagnosticOption,
            compilationOptions.WarningLevel,
            compilationOptions.AllowUnsafe,
            compilationOptions.CheckOverflow,
            compilationOptions.Deterministic,
            specificDiagnostics,
            globalCompilerDiagnosticOptions,
            documents,
            additionalDocuments,
            analyzerConfigDocuments,
            metadataReferences,
            project.ProjectReferences.Select(reference => reference.ProjectId.Id.ToString("D")).ToImmutableArray(),
            compilerErrorCount));

        Console.Error.WriteLine(
            $"manifest: {project.Name}: {documents.Length} source(s), " +
            $"{metadataReferences.Length} reference(s), " +
            (compilerErrorCount is null
                ? "compiler diagnostics skipped"
                : $"{compilerErrorCount} compiler error(s)"));
        foreach (var diagnostic in compilerErrors.Take(3))
        {
            Console.Error.WriteLine($"manifest:   {diagnostic.Id}: {diagnostic.GetMessage()}");
        }
    }

    ImmutableArray<ManifestMessage> workspaceMessageSnapshot;
    lock (workspaceMessagesLock)
    {
        workspaceMessageSnapshot = workspaceMessages.ToImmutable();
    }

    var manifest = new CompilationManifest(
        CompilationManifest.CurrentVersion,
        target,
        DateTimeOffset.UtcNow,
        projects.ToImmutable(),
        workspaceMessageSnapshot);
    var fullOutput = Path.GetFullPath(output);
    Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
    var temporaryOutput = fullOutput + ".tmp";
    var serializationStarted = Stopwatch.GetTimestamp();
    await using (var stream = File.Create(temporaryOutput))
    {
        await JsonSerializer.SerializeAsync(
            stream,
            manifest,
            CompilationManifestJsonContext.Default.CompilationManifest);
    }

    File.Move(temporaryOutput, fullOutput, true);
    var serializationElapsed = Stopwatch.GetElapsedTime(serializationStarted);
    stopwatch.Stop();
    Console.Error.WriteLine(
        $"manifest: wrote {projects.Count} project(s) to '{fullOutput}' in {stopwatch.Elapsed.TotalSeconds:F2}s");
    Console.Error.WriteLine(
        $"manifest: phases: open={openElapsed.TotalSeconds:F2}s, " +
        $"compilations={compilationElapsed.TotalSeconds:F2}s, " +
        $"generators={generatorElapsed.TotalSeconds:F2}s, " +
        $"documents={documentElapsed.TotalSeconds:F2}s, " +
        $"diagnostics={diagnosticsElapsed.TotalSeconds:F2}s, " +
        $"serialize={serializationElapsed.TotalSeconds:F2}s");
    var workspaceFailed = workspaceMessageSnapshot.Any(message => message.Kind == "Failure");
    return workspaceFailed || (!allowCompilerErrors && totalCompilerErrors > 0) ? 1 : 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"DrillPress.BuildHost: {exception}");
    return 2;
}

static bool TryParseArguments(
    string[] arguments,
    out string target,
    out string output,
    out Dictionary<string, string> properties,
    out bool allowCompilerErrors,
    out bool skipCompilerDiagnostics)
{
    target = string.Empty;
    output = string.Empty;
    properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    allowCompilerErrors = false;
    skipCompilerDiagnostics = false;
    if (arguments.Length < 3 || arguments[0] != "export")
    {
        return false;
    }

    target = arguments[1];
    output = arguments[2];
    for (var index = 3; index < arguments.Length; index++)
    {
        if (arguments[index] == "--allow-compiler-errors")
        {
            allowCompilerErrors = true;
            continue;
        }

        if (arguments[index] == "--skip-compiler-diagnostics")
        {
            skipCompilerDiagnostics = true;
            continue;
        }

        if (arguments[index] != "--property" || index + 1 >= arguments.Length)
        {
            return false;
        }

        var property = arguments[++index];
        var separator = property.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        properties[property[..separator]] = property[(separator + 1)..];
    }

    return true;
}

static string ResolveTarget(string target)
{
    var fullTarget = Path.GetFullPath(target);
    if (File.Exists(fullTarget))
    {
        return fullTarget;
    }

    if (!Directory.Exists(fullTarget))
    {
        throw new FileNotFoundException($"Target '{target}' does not exist.");
    }

    var candidates = Directory.EnumerateFiles(fullTarget, "*.slnx", SearchOption.TopDirectoryOnly)
        .Concat(Directory.EnumerateFiles(fullTarget, "*.sln", SearchOption.TopDirectoryOnly))
        .Concat(Directory.EnumerateFiles(fullTarget, "*.csproj", SearchOption.TopDirectoryOnly))
        .ToArray();
    return candidates.Length == 1
        ? candidates[0]
        : throw new InvalidOperationException(
            $"Directory '{target}' must contain exactly one solution or project; found {candidates.Length}.");
}

static bool IsTestProject(Project project)
{
    var path = project.FilePath ?? string.Empty;
    var name = project.Name;
    return name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
           name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase) ||
           path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               .Any(segment => segment.Equals("test", StringComparison.OrdinalIgnoreCase) ||
                               segment.Equals("tests", StringComparison.OrdinalIgnoreCase)) ||
           project.MetadataReferences.Any(reference =>
               Path.GetFileNameWithoutExtension(reference.Display ?? string.Empty) is
                   "xunit.core" or "nunit.framework" or "Microsoft.VisualStudio.TestPlatform.TestFramework");
}

static bool IsUnderBuildOutput(string path, string? projectPath)
{
    if (projectPath is null)
    {
        return false;
    }

    var projectDirectory = Path.GetDirectoryName(projectPath)!;
    var relative = Path.GetRelativePath(projectDirectory, path);
    var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault();
    return firstSegment is not null &&
           (firstSegment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("bin", StringComparison.OrdinalIgnoreCase));
}

static async Task<ImmutableArray<ManifestDocument>> CaptureTextDocumentsAsync(
    IEnumerable<TextDocument> documents,
    string projectName)
{
    var result = ImmutableArray.CreateBuilder<ManifestDocument>();
    var index = 0;
    foreach (var document in documents)
    {
        var path = string.IsNullOrWhiteSpace(document.FilePath)
            ? $"drillpress-document://{projectName}/{index++:D6}/{document.Name}"
            : Path.GetFullPath(document.FilePath);
        var text = await document.GetTextAsync();
        result.Add(new ManifestDocument(path, text.ToString(), IsGenerated: false, []));
    }

    return result.OrderBy(static document => document.Path, StringComparer.Ordinal).ToImmutableArray();
}

static ImmutableArray<string> GetConfiguredCompilerDiagnosticIds(
    IEnumerable<ManifestDocument> analyzerConfigDocuments)
{
    return analyzerConfigDocuments
        .SelectMany(document => Regex.Matches(
            document.Text,
            @"dotnet_diagnostic\.(CS\d+)\.severity",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        .Select(static match => match.Groups[1].Value.ToUpperInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static id => id, StringComparer.Ordinal)
        .ToImmutableArray();
}

static ImmutableDictionary<string, int> CaptureCompilerDiagnosticOptions(
    SyntaxTreeOptionsProvider? optionsProvider,
    SyntaxTree tree,
    IEnumerable<string> diagnosticIds)
{
    if (optionsProvider is null)
    {
        return ImmutableDictionary<string, int>.Empty;
    }

    var result = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var diagnosticId in diagnosticIds)
    {
        if (optionsProvider.TryGetDiagnosticValue(tree, diagnosticId, CancellationToken.None, out var value))
        {
            result[diagnosticId] = (int)value;
        }
    }

    return result.ToImmutable();
}

static ImmutableDictionary<string, int> CaptureGlobalCompilerDiagnosticOptions(
    SyntaxTreeOptionsProvider? optionsProvider,
    IEnumerable<string> diagnosticIds)
{
    if (optionsProvider is null)
    {
        return ImmutableDictionary<string, int>.Empty;
    }

    var result = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var diagnosticId in diagnosticIds)
    {
        if (optionsProvider.TryGetGlobalDiagnosticValue(diagnosticId, CancellationToken.None, out var value))
        {
            result[diagnosticId] = (int)value;
        }
    }

    return result.ToImmutable();
}

static StringComparer GetPathComparer() => OperatingSystem.IsWindows()
    ? StringComparer.OrdinalIgnoreCase
    : StringComparer.Ordinal;
