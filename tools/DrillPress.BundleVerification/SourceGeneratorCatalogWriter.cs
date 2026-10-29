using System.IO.Abstractions;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DrillPress.Manifest;

namespace DrillPress.BundleVerification;

public sealed class SourceGeneratorCatalogWriter(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    public void Write(string inventoryPath, string outputDirectory)
    {
        var inventory =
            JsonSerializer.Deserialize(
                _fileSystem.File.ReadAllBytes(inventoryPath),
                GeneratorPlanJsonContext.Default.GeneratorBuildInventory
            ) ?? throw new InvalidDataException("Missing source generator inventory.");
        ComponentVersion.RequireMatch(inventory.ProductVersion, "Generator inventory");
        var entries = Select(inventory.Generators);
        var assemblies = entries.Select(entry => entry.AssemblyPath).Distinct().Order().ToArray();
        var dependencies = Dependencies(assemblies, inventory.AnalyzerPaths).ToArray();
        var aliases = assemblies
            .Select((path, index) => (path, name: "generator" + index))
            .ToDictionary(item => item.path, item => item.name);
        var output = _fileSystem.Path.GetFullPath(outputDirectory);
        _fileSystem.Directory.CreateDirectory(output);
        var sourcePath = _fileSystem.Path.Combine(output, "SourceGenerators.g.cs");
        var propsPath = _fileSystem.Path.Combine(output, "SourceGenerators.props");
        WriteDifferent(sourcePath, Source(entries, aliases));
        var references = new XElement(
            "ItemGroup",
            new XElement("Compile", new XAttribute("Include", sourcePath))
        );
        foreach (var path in assemblies)
            references.Add(Reference(path, aliases[path]));
        foreach (var path in dependencies.Except(assemblies).Order())
            references.Add(Reference(path, "global"));
        var runtime = new XElement(
            "PropertyGroup",
            new XElement("ConcurrentGarbageCollection", "false"),
            new XElement("ThreadPoolMinThreads", "32")
        );
        runtime.Add(
            new XComment(
                "Roslyn's reviewed NativeAOT warnings remain visible; native parity verifies the exercised compiler paths."
            )
        );
        runtime.Add(new XElement("WarningsNotAsErrors", "$(WarningsNotAsErrors);IL3000;IL2104"));
        if (
            assemblies.Any(path =>
                _fileSystem.Path.GetFileName(path) == "Microsoft.CodeAnalysis.Razor.Compiler.dll"
            )
        )
        {
            runtime.Add(
                new XComment(
                    "The SDK Razor generator carries compiler-host PE architecture metadata. NativeAOT consumes its managed IL; retain the SDK warning during publication."
                )
            );
            runtime.Add(new XElement("WarningsNotAsErrors", "$(WarningsNotAsErrors);CS8012"));
        }
        var configuration = new XElement(
            "ItemGroup",
            new XElement(
                "RuntimeHostConfigurationOption",
                new XAttribute("Include", "System.GC.HeapCount"),
                new XAttribute("Value", "$(DrillPressCaptureHeapCount)")
            ),
            new XElement(
                "RuntimeHostConfigurationOption",
                new XAttribute("Include", "System.GC.DynamicAdaptationMode"),
                new XAttribute("Value", "0")
            )
        );
        WriteDifferent(
            propsPath,
            new XDocument(new XElement("Project", runtime, configuration, references)).ToString()
                + "\n"
        );
        WriteDifferent(
            _fileSystem.Path.Combine(output, "assembly-inputs.txt"),
            string.Join("\n", assemblies.Concat(dependencies).Distinct().Order()) + "\n"
        );
        var platform =
            OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsMacOS() ? "osx"
            : "linux";
        WriteDifferent(
            _fileSystem.Path.Combine(output, "runtime-identifier.txt"),
            platform
                + "-"
                + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
                + "\n"
        );
        WriteDifferent(
            _fileSystem.Path.Combine(output, "heap-count.txt"),
            Math.Min(16, Environment.ProcessorCount)
                .ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n"
        );
        WriteDifferent(
            _fileSystem.Path.Combine(output, "platform-target.txt"),
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant() + "\n"
        );
    }

    private static GeneratorBuildEntry[] Select(GeneratorBuildEntry[] entries) =>
        entries
            .GroupBy(entry => new AssemblyName(entry.Description.AssemblyName).Name!)
            .SelectMany(group =>
            {
                var version = group.Max(entry =>
                    new AssemblyName(entry.Description.AssemblyName).Version
                );
                var latest = group
                    .Where(entry =>
                        new AssemblyName(entry.Description.AssemblyName).Version == version
                    )
                    .ToArray();
                return latest.Select(entry => entry.Description.Fingerprint).Distinct().Count() == 1
                    ? latest.Where(entry => entry.CanLink)
                    : [];
            })
            .OrderBy(entry => entry.Description.AssemblyName)
            .ThenBy(entry => entry.Description.TypeName)
            .ToArray();

    private static XElement Reference(string path, string alias) =>
        new(
            "Reference",
            new XAttribute("Include", path),
            new XElement("Aliases", alias),
            new XElement("Private", "true"),
            new XElement("SpecificVersion", "true")
        );

    private static string Source(
        GeneratorBuildEntry[] entries,
        IReadOnlyDictionary<string, string> aliases
    )
    {
        var text = new StringBuilder("// <auto-generated/>\n");
        foreach (var alias in aliases.Values)
            text.Append("extern alias ").Append(alias).Append(";\n");
        text.Append(
            "using Microsoft.CodeAnalysis;\n[assembly: global::DrillPress.Engine.SourceGeneratorCatalogAttribute(typeof(global::DrillPress.Generated.BundleSourceGenerators))]\nnamespace DrillPress.Generated;\n/// <summary>Source generators linked from the target's verified compiler inventory.</summary>\npublic sealed class BundleSourceGenerators : global::DrillPress.Engine.SourceGeneratorCatalog\n{\n/// <summary>Creates the exact generator implementations selected when publishing this bundle.</summary>\npublic BundleSourceGenerators() : base([\n"
        );
        foreach (var entry in entries)
        {
            var name = string.Join(
                ".",
                entry.Description.TypeName.Replace('+', '.').Split('.').Select(part => "@" + part)
            );
            text.Append("new global::DrillPress.Engine.CompiledSourceGenerator(new ")
                .Append(aliases[entry.AssemblyPath])
                .Append("::")
                .Append(name)
                .Append("()");
            if (entry.Incremental)
                text.Append(".AsSourceGenerator()");
            text.Append(", \"").Append(entry.Description.Fingerprint).Append("\"),\n");
        }
        return text.Append("]) {}\n}\n").ToString();
    }

    private IEnumerable<string> Dependencies(string[] assemblies, string[] available)
    {
        var seen = new HashSet<string>();
        var pending = new Queue<string>(assemblies);
        while (pending.TryDequeue(out var path))
        {
            if (!seen.Add(path))
                continue;
            yield return path;
            foreach (var name in ReadDependencies(path).Where(name => !IsFramework(name)))
            {
                var local = _fileSystem.Path.Combine(
                    _fileSystem.Path.GetDirectoryName(path)!,
                    name + ".dll"
                );
                var resolved = _fileSystem.File.Exists(local)
                    ? local
                    : available.FirstOrDefault(candidate =>
                        _fileSystem.Path.GetFileName(candidate) == name + ".dll"
                    );
                if (resolved is not null)
                    pending.Enqueue(resolved);
            }
        }
    }

    private string[] ReadDependencies(string path)
    {
        using var stream = _fileSystem.File.OpenRead(path);
        using var image = new PEReader(stream);
        var reader = image.GetMetadataReader();
        return reader
            .AssemblyReferences.Select(reference =>
                reader.GetString(reader.GetAssemblyReference(reference).Name)
            )
            .ToArray();
    }

    private static bool IsFramework(string name) =>
        name.StartsWith("System.", StringComparison.Ordinal)
        || name
            is "System"
                or "mscorlib"
                or "netstandard"
                or "Microsoft.CSharp"
                or "Microsoft.CodeAnalysis"
                or "Microsoft.CodeAnalysis.CSharp"
                or "Microsoft.CodeAnalysis.VisualBasic";

    private void WriteDifferent(string path, string contents)
    {
        if (!_fileSystem.File.Exists(path) || _fileSystem.File.ReadAllText(path) != contents)
            _fileSystem.File.WriteAllText(path, contents, new UTF8Encoding(false));
    }
}
