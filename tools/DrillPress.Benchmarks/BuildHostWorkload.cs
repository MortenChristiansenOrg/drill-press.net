using System.IO.Abstractions;

namespace DrillPress.Benchmarks;

/// <summary>
/// A synthetic layered solution for measuring BuildHost loading. Every context runs the SDK's regex
/// and JSON source generators, and dependents bind types of the projects they reference, so both
/// parallel capture and the generator chain along the dependency graph show in the measurement.
/// </summary>
public sealed class BuildHostWorkload(IFileSystem fileSystem)
{
    /// <summary>Source files per project, each with a generated regex.</summary>
    public const int FilesPerProject = 40;

    private static readonly (string Name, string[] References)[] _projects =
    [
        ("Core", []),
        ("Feature0", ["Core"]),
        ("Feature1", ["Core"]),
        ("Feature2", ["Core"]),
        ("Feature3", ["Core"]),
        ("App", ["Feature0", "Feature1", "Feature2", "Feature3"]),
        ("AppTests", ["App", "Core"]),
    ];

    private readonly IFileSystem _fileSystem = fileSystem;

    /// <summary>Writes the solution into <paramref name="directory"/> and returns its path.</summary>
    public string Write(string directory)
    {
        _fileSystem.Directory.CreateDirectory(directory);
        // Isolates the workload from repository build settings and central package versions.
        foreach (
            var name in new[]
            {
                "Directory.Build.props",
                "Directory.Build.targets",
                "Directory.Packages.props",
            }
        )
            _fileSystem.File.WriteAllText(
                _fileSystem.Path.Combine(directory, name),
                "<Project />\n"
            );
        foreach (var (name, references) in _projects)
            WriteProject(_fileSystem.Path.Combine(directory, name), name, references);
        var solution = _fileSystem.Path.Combine(directory, "Workload.slnx");
        _fileSystem.File.WriteAllText(
            solution,
            "<Solution>\n"
                + string.Concat(
                    _projects.Select(project =>
                        $"  <Project Path=\"{project.Name}/{project.Name}.csproj\" />\n"
                    )
                )
                + "</Solution>\n"
        );
        return solution;
    }

    private void WriteProject(string directory, string name, string[] references)
    {
        _fileSystem.Directory.CreateDirectory(directory);
        _fileSystem.File.WriteAllText(
            _fileSystem.Path.Combine(directory, $"{name}.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
            {string.Concat(
                references.Select(reference =>
                    $"    <ProjectReference Include=\"../{reference}/{reference}.csproj\" />\n"
                )
            )}  </ItemGroup>
            </Project>

            """.ReplaceLineEndings(
                "\n"
            )
        );
        var upstream = references.Length > 0 ? $"{references[0]}.Service0" : "object";
        for (var index = 0; index < FilesPerProject; index++)
            _fileSystem.File.WriteAllText(
                _fileSystem.Path.Combine(directory, $"Service{index}.cs"),
                $$"""
                using System.Text.RegularExpressions;

                namespace {{name}};

                public partial class Service{{index}}
                {
                    public {{upstream}}? Upstream { get; init; }

                    public string Name { get; init; } = "";

                    public bool Matches(string value) => Pattern().IsMatch(value);

                    [GeneratedRegex("^{{name.ToLowerInvariant()}}-{{index}}-[a-z]+$")]
                    private static partial Regex Pattern();
                }

                """.ReplaceLineEndings("\n")
            );
        _fileSystem.File.WriteAllText(
            _fileSystem.Path.Combine(directory, "Serialization.cs"),
            $$"""
            using System.Text.Json.Serialization;

            namespace {{name}};

            {{string.Concat(
                Enumerable.Range(0, FilesPerProject).Select(index =>
                    $"[JsonSerializable(typeof(Service{index}))]\n"
                )
            )}}internal sealed partial class SerializationContext : JsonSerializerContext { }

            """.ReplaceLineEndings(
                "\n"
            )
        );
    }
}
