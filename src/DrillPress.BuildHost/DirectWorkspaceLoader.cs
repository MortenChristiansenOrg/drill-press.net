using System.Collections.Immutable;
using System.IO.Abstractions;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using BuildGraph = Microsoft.Build.Graph.ProjectGraph;

namespace DrillPress.BuildHost;

internal sealed class DirectWorkspaceLoader(IFileSystem fileSystem) : IDisposable
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private ProjectCollection? _collection;

    public void Dispose() => _collection?.Dispose();

    internal Solution? Load(
        MSBuildWorkspace workspace,
        string target,
        IDictionary<string, string> properties,
        CancellationToken cancellationToken,
        Action<Microsoft.CodeAnalysis.Project>? loaded = null
    )
    {
        var globals = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase)
        {
            ["DesignTimeBuild"] = "true",
            ["NonExistentFile"] = "__NonExistentSubDir__/__NonExistentFile__",
            ["BuildProjectReferences"] = "false",
            ["BuildingProject"] = "false",
            ["ProvideCommandLineArgs"] = "true",
            ["SkipCompilerExecution"] = "true",
            ["ContinueOnError"] = "ErrorAndContinue",
            ["ShouldUnsetParentConfigurationAndPlatform"] = "false",
        };
        var collection = _collection = new ProjectCollection(globals);
        var graph = new BuildGraph(
            [new ProjectGraphEntryPoint(target, globals)],
            collection,
            (path, globalProperties, projectCollection) =>
            {
                return new ProjectInstance(path, globalProperties, null, projectCollection);
            },
            cancellationToken
        );
        var nodes = graph.ProjectNodesTopologicallySorted.ToArray();
        if (
            nodes.Length == 0
            || nodes.Any(node =>
                node.ProjectInstance.GetPropertyValue("TargetFramework").Length == 0
                || !_fileSystem
                    .Path.GetExtension(node.ProjectInstance.FullPath)
                    .Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            )
        )
            return null;
        var logger = new CaptureBuildLogger();
        using var manager = new BuildManager("DrillPress capture");
        manager.BeginBuild(
            new BuildParameters(collection)
            {
                EnableNodeReuse = false,
                MaxNodeCount = 1,
                Loggers = [logger],
            }
        );
        var states = new Dictionary<ProjectGraphNode, ProjectInstance>();
        var metadata = new Dictionary<string, PortableExecutableReference>();
        var infos = new Dictionary<ProjectGraphNode, ProjectInfo>();
        var solution = workspace.CurrentSolution;
        try
        {
            using var cancellation = cancellationToken.Register(manager.CancelAllSubmissions);
            foreach (var node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = manager.BuildRequest(
                    new BuildRequestData(
                        node.ProjectInstance,
                        ["Compile", "CoreCompile"],
                        null,
                        BuildRequestDataFlags.ProvideProjectStateAfterBuild
                    )
                );
                cancellationToken.ThrowIfCancellationRequested();
                if (result.OverallResult != BuildResultCode.Success)
                    throw new InvalidOperationException(string.Join("\n", logger.Errors));
                var state = result.ProjectStateAfterBuild ?? node.ProjectInstance;
                states.Add(node, state);
                var references = ReadEdges(node, state).ToArray();
                var info = CreateInfo(
                    state,
                    references.SelectMany(edge => OutputPaths(states[edge.Node])).ToHashSet()
                );
                info = info.WithProjectReferences(
                    references
                        .Select(edge => new ProjectReference(
                            infos[edge.Node].Id,
                            edge.Item.GetMetadataValue("Aliases")
                                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                                .ToImmutableArray(),
                            edge.Item.GetMetadataValue("EmbedInteropTypes")
                                .Equals("true", StringComparison.OrdinalIgnoreCase)
                        ))
                        .Distinct()
                );
                infos.Add(node, info);
                solution = solution.AddProject(info);
                loaded?.Invoke(solution.GetProject(info.Id)!);
            }
        }
        finally
        {
            manager.EndBuild();
        }

        return solution;

        ProjectInfo CreateInfo(ProjectInstance state, IReadOnlySet<string> sourceOutputs)
        {
            var arguments = state
                .GetItems("CscCommandLineArgs")
                .Select(item => item.EvaluatedInclude)
                .ToArray();
            if (arguments.Length == 0)
                throw new InvalidOperationException(
                    "No compiler arguments for " + state.GetPropertyValue("MSBuildProjectFullPath")
                );
            var parsed = CSharpCommandLineParser.Default.Parse(
                arguments,
                state.GetPropertyValue("MSBuildProjectDirectory"),
                null
            );
            var info = CommandLineProject
                .CreateProjectInfo(
                    state.GetPropertyValue("MSBuildProjectName"),
                    LanguageNames.CSharp,
                    arguments.Where(argument => !IsReference(argument)),
                    state.GetPropertyValue("MSBuildProjectDirectory"),
                    workspace
                )
                .WithFilePath(state.GetPropertyValue("MSBuildProjectFullPath"));
            if (
                info.ParseOptions is CSharpParseOptions
                {
                    DocumentationMode: DocumentationMode.None
                } parse
            )
                info = info.WithParseOptions(parse.WithDocumentationMode(DocumentationMode.Parse));
            var references = new List<MetadataReference>();
            foreach (var reference in parsed.MetadataReferences)
            {
                var path = _fileSystem.Path.GetFullPath(
                    reference.Reference,
                    state.GetPropertyValue("MSBuildProjectDirectory")
                );
                if (sourceOutputs.Contains(path))
                    continue;
                if (!metadata.TryGetValue(path, out var image))
                {
                    image = MetadataReference.CreateFromImage(
                        _fileSystem.File.ReadAllBytes(path),
                        filePath: path
                    );
                    metadata.Add(path, image);
                }
                references.Add(image.WithProperties(reference.Properties));
            }
            return info.WithMetadataReferences(references);
        }
    }

    private IEnumerable<(ProjectGraphNode Node, ProjectItemInstance Item)> ReadEdges(
        ProjectGraphNode node,
        ProjectInstance state
    )
    {
        var references = state
            .GetItems("_MSBuildProjectReferenceExistent")
            .Where(item =>
                !item.GetMetadataValue("ReferenceOutputAssembly")
                    .Equals("false", StringComparison.OrdinalIgnoreCase)
            )
            .ToLookup(item =>
                _fileSystem.Path.GetFullPath(
                    item.EvaluatedInclude,
                    state.GetPropertyValue("MSBuildProjectDirectory")
                )
            );
        return node.ProjectReferences.SelectMany(dependency =>
            references[dependency.ProjectInstance.FullPath].Select(item => (dependency, item))
        );
    }

    private IEnumerable<string> OutputPaths(ProjectInstance state) =>
        new[] { "TargetPath", "TargetRefPath", "IntermediateAssembly", "OutputRefFilePath" }
            .Select(state.GetPropertyValue)
            .Concat(state.GetItems("IntermediateAssembly").Select(item => item.EvaluatedInclude))
            .Where(path => path.Length > 0)
            .Select(path =>
                _fileSystem.Path.GetFullPath(
                    path,
                    state.GetPropertyValue("MSBuildProjectDirectory")
                )
            );

    private static bool IsReference(string argument) =>
        new[]
        {
            "/reference:",
            "/r:",
            "/link:",
            "/addmodule:",
            "-reference:",
            "-r:",
            "-link:",
            "-addmodule:",
        }.Any(prefix => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
