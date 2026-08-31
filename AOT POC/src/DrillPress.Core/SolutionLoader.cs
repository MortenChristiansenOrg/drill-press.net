using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

public static partial class SolutionLoader
{
    public static async Task<AnalysisSolution> LoadAsync(string target, CancellationToken cancellationToken = default)
    {
        var fullTarget = Path.GetFullPath(target);
        var specs = DiscoverProjects(fullTarget);
        if (specs.Count == 0)
        {
            throw new InvalidOperationException($"No C# projects or files were found for '{target}'.");
        }

        var referencePaths = DiscoverReferenceAssemblies();
        var frameworkReferences = referencePaths
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();
        var projectsByPath = new Dictionary<string, ProjectModel>(PathComparer);
        var compilationsByPath = new Dictionary<string, CSharpCompilation>(PathComparer);
        var pending = new Queue<ProjectSpec>(specs);
        var attemptsWithoutProgress = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var spec = pending.Dequeue();
            var unresolved = spec.ProjectReferences.Where(reference =>
                specs.Any(candidate => PathComparer.Equals(candidate.Path, reference)) &&
                !compilationsByPath.ContainsKey(reference));
            if (unresolved.Any())
            {
                pending.Enqueue(spec);
                attemptsWithoutProgress++;
                if (attemptsWithoutProgress >= pending.Count)
                {
                    throw new InvalidOperationException("The project graph contains a cycle or an unresolved project reference.");
                }

                continue;
            }

            attemptsWithoutProgress = 0;
            var project = new ProjectModel(spec.Name, spec.Path, spec.IsTestProject);
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp14, DocumentationMode.Parse);
            var trees = ImmutableArray.CreateBuilder<SyntaxTree>();
            foreach (var sourcePath in spec.SourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = await File.ReadAllTextAsync(sourcePath, cancellationToken);
                trees.Add(CSharpSyntaxTree.ParseText(
                    SourceText.From(source, Encoding.UTF8),
                    parseOptions,
                    sourcePath,
                    cancellationToken: cancellationToken));
            }

            // This POC models the SDK's default implicit usings without loading
            // MSBuild into the AOT process. A production manifest broker would
            // supply the evaluated list for each project.
            var implicitUsingsTree = CSharpSyntaxTree.ParseText(
                """
                global using System;
                global using System.Collections.Generic;
                global using System.IO;
                global using System.Linq;
                global using System.Net.Http;
                global using System.Threading;
                global using System.Threading.Tasks;
                """,
                parseOptions,
                path: $"{spec.Path}.ImplicitUsings.g.cs",
                cancellationToken: cancellationToken);

            var references = frameworkReferences.AddRange(
                spec.ProjectReferences
                    .Where(compilationsByPath.ContainsKey)
                    .Select(reference => compilationsByPath[reference].ToMetadataReference()));
            var compilation = CSharpCompilation.Create(
                spec.Name,
                trees.Append(implicitUsingsTree),
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable,
                    concurrentBuild: true));
            project.Compilation = compilation;
            project.Documents = trees
                .Select(tree => new SourceDocument(project, tree.FilePath, tree))
                .ToImmutableArray();
            projectsByPath.Add(spec.Path, project);
            compilationsByPath.Add(spec.Path, compilation);
        }

        return new AnalysisSolution(projectsByPath.Values.OrderBy(project => project.Path, PathComparer).ToImmutableArray());
    }

    private static List<ProjectSpec> DiscoverProjects(string target)
    {
        if (File.Exists(target))
        {
            return Path.GetExtension(target).ToLowerInvariant() switch
            {
                ".sln" => ReadClassicSolution(target),
                ".slnx" => ReadXmlSolution(target),
                ".csproj" => ReadProjectClosure(target),
                ".cs" => [ProjectSpec.ForLooseFiles([target])],
                _ => throw new InvalidOperationException($"Unsupported target '{target}'."),
            };
        }

        if (Directory.Exists(target))
        {
            return Directory.EnumerateFiles(target, "*.csproj", SearchOption.AllDirectories)
                .Where(static path => !IsBuildOutput(path))
                .Select(ReadProject)
                .ToList();
        }

        if (target.IndexOfAny(['*', '?']) >= 0)
        {
            var files = ExpandPattern(target).ToArray();
            return files.Length == 0 ? [] : [ProjectSpec.ForLooseFiles(files)];
        }

        return [];
    }

    private static List<ProjectSpec> ReadClassicSolution(string solutionPath)
    {
        var directory = Path.GetDirectoryName(solutionPath)!;
        return File.ReadLines(solutionPath)
            .Select(line => ClassicProjectPattern().Match(line))
            .Where(static match => match.Success)
            .Select(match => Path.GetFullPath(Path.Combine(directory, match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))))
            .Distinct(PathComparer)
            .Select(ReadProject)
            .ToList();
    }

    private static List<ProjectSpec> ReadXmlSolution(string solutionPath)
    {
        var directory = Path.GetDirectoryName(solutionPath)!;
        var document = XDocument.Load(solutionPath, LoadOptions.None);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.Combine(directory, path!)))
            .Distinct(PathComparer)
            .Select(ReadProject)
            .ToList();
    }

    private static List<ProjectSpec> ReadProjectClosure(string projectPath)
    {
        var result = new Dictionary<string, ProjectSpec>(PathComparer);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(projectPath));
        while (pending.TryPop(out var path))
        {
            if (result.ContainsKey(path))
            {
                continue;
            }

            var project = ReadProject(path);
            result.Add(path, project);
            foreach (var reference in project.ProjectReferences)
            {
                pending.Push(reference);
            }
        }

        return result.Values.ToList();
    }

    private static ProjectSpec ReadProject(string projectPath)
    {
        projectPath = Path.GetFullPath(projectPath);
        var directory = Path.GetDirectoryName(projectPath)!;
        var document = XDocument.Load(projectPath, LoadOptions.None);
        var values = document.Descendants()
            .Where(element => element.Name.LocalName is "AssemblyName" or "IsTestProject")
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        var name = values.GetValueOrDefault("AssemblyName") ?? Path.GetFileNameWithoutExtension(projectPath);
        var isTest = bool.TryParse(values.GetValueOrDefault("IsTestProject"), out var parsed) && parsed;
        var references = document.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(directory, include!)))
            .ToImmutableArray();
        var sourceFiles = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsBuildOutput(path))
            .OrderBy(static path => path, PathComparer)
            .ToImmutableArray();
        return new ProjectSpec(name, projectPath, isTest, sourceFiles, references);
    }

    private static IEnumerable<string> ExpandPattern(string fullPattern)
    {
        var wildcardIndex = fullPattern.IndexOfAny(['*', '?']);
        var prefix = fullPattern[..wildcardIndex];
        var separatorIndex = prefix.LastIndexOf(Path.DirectorySeparatorChar);
        var root = separatorIndex < 0 ? Directory.GetCurrentDirectory() : prefix[..separatorIndex];
        if (string.IsNullOrEmpty(root))
        {
            root = Path.GetPathRoot(fullPattern)!;
        }

        var regexPattern = "^" + Regex.Escape(fullPattern)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal)
            .Replace("\\*", $"[^{Regex.Escape(Path.DirectorySeparatorChar.ToString())}]*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        var regex = new Regex(regexPattern, RegexOptions.CultureInvariant);
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(path => regex.IsMatch(path))
            : [];
    }

    private static ImmutableArray<string> DiscoverReferenceAssemblies()
    {
        foreach (var dotnetRoot in DotnetRoots())
        {
            var packRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
            if (!Directory.Exists(packRoot))
            {
                continue;
            }

            var refDirectory = Directory.EnumerateDirectories(packRoot)
                .OrderByDescending(static path => ParseVersion(Path.GetFileName(path)))
                .Select(path => Path.Combine(path, "ref", "net10.0"))
                .FirstOrDefault(Directory.Exists);
            if (refDirectory is not null)
            {
                return Directory.EnumerateFiles(refDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                    .OrderBy(static path => path, PathComparer)
                    .ToImmutableArray();
            }
        }

        throw new InvalidOperationException("The .NET 10 reference pack could not be located. Set DOTNET_ROOT.");
    }

    private static IEnumerable<string> DotnetRoots()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFiles, "dotnet");
        }
        else
        {
            yield return "/usr/share/dotnet";
            yield return "/usr/lib/dotnet";
        }
    }

    private static Version ParseVersion(string value) =>
        Version.TryParse(value, out var version) ? version : new Version();

    private static bool IsBuildOutput(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
               segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    [GeneratedRegex("Project\\([^)]*\\)\\s*=\\s*\"[^\"]*\",\\s*\"([^\"]+\\.csproj)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ClassicProjectPattern();

    private sealed record ProjectSpec(
        string Name,
        string Path,
        bool IsTestProject,
        ImmutableArray<string> SourceFiles,
        ImmutableArray<string> ProjectReferences)
    {
        public static ProjectSpec ForLooseFiles(IEnumerable<string> files) => new(
            "LooseFiles",
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "LooseFiles.csproj"),
            false,
            files.Select(System.IO.Path.GetFullPath).ToImmutableArray(),
            []);
    }
}
