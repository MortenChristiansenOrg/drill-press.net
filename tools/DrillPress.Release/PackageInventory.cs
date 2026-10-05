using System.IO.Abstractions;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace DrillPress.Release;

public sealed class PackageInventory(IFileSystem fileSystem, AssemblyVersionReader assemblyVersions)
{
    public static IReadOnlyList<string> PackageIds { get; } =
        Array.AsReadOnly(
            new[]
            {
                "DrillPress.Manifest",
                "DrillPress.RuleAuthoring",
                "DrillPress.Linq",
                "DrillPress.Engine",
                "DrillPress.Testing",
                "DrillPress.Cli",
            }
        );

    public IReadOnlyList<ReleasePackage> Read(string directory, ReleaseVersion version)
    {
        var packages = fileSystem
            .Directory.GetFiles(directory, "*.nupkg")
            .Select(path => ReadPackage(path, version.Version))
            .ToArray();
        if (!packages.Select(package => package.Id).Order().SequenceEqual(PackageIds.Order()))
        {
            throw new InvalidDataException(
                "Release must contain exactly the six Drill Press packages."
            );
        }

        return PackageIds.Select(id => packages.Single(package => package.Id == id)).ToArray();
    }

    private ReleasePackage ReadPackage(string path, string version)
    {
        using var stream = fileSystem.File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var manifest = archive
            .Entries.Single(entry => entry.FullName.EndsWith(".nuspec"))
            .Open();
        var document = XDocument.Load(manifest);
        var metadata = document
            .Root!.Elements()
            .Single(element => element.Name.LocalName == "metadata");
        var id = metadata.Elements().Single(element => element.Name.LocalName == "id").Value;
        var actualVersion = metadata
            .Elements()
            .Single(element => element.Name.LocalName == "version")
            .Value;
        if (!PackageIds.Contains(id) || actualVersion != version)
        {
            throw new InvalidDataException(
                $"Unexpected package {id} {actualVersion}; expected {version}."
            );
        }

        ValidateDependencies(metadata, id, version);
        ValidateAssemblies(archive, id, version);
        return new ReleasePackage(id, version, path, HashContents(archive));
    }

    private static void ValidateDependencies(XElement metadata, string id, string version)
    {
        var dependencies = metadata
            .Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .Where(element => element.Attribute("id")!.Value.StartsWith("DrillPress."))
            .ToArray();
        foreach (var dependency in dependencies)
        {
            var dependencyId = dependency.Attribute("id")!.Value;
            if (
                !PackageIds.Contains(dependencyId)
                || dependency.Attribute("version")?.Value != $"[{version}]"
            )
            {
                throw new InvalidDataException($"{id} must pin {dependencyId} to [{version}].");
            }
        }

        string[] expected = id switch
        {
            "DrillPress.Engine" => ["DrillPress.Manifest", "DrillPress.RuleAuthoring"],
            "DrillPress.RuleAuthoring" => ["DrillPress.Manifest"],
            "DrillPress.Linq" => ["DrillPress.RuleAuthoring"],
            "DrillPress.Testing" => ["DrillPress.Engine"],
            _ => [],
        };
        if (
            !dependencies
                .Select(dependency => dependency.Attribute("id")!.Value)
                .Order()
                .SequenceEqual(expected.Order())
        )
        {
            throw new InvalidDataException(
                $"{id} has an unexpected internal dependency inventory."
            );
        }
    }

    private void ValidateAssemblies(ZipArchive archive, string id, string version)
    {
        var assemblies = archive
            .Entries.Where(entry =>
                entry.Name.StartsWith("DrillPress.") && entry.Name.EndsWith(".dll")
            )
            .ToArray();
        var requiredAssembly =
            id == "DrillPress.Cli"
                ? "tools/net10.0/any/DrillPress.Cli.dll"
                : $"lib/net10.0/{id}.dll";
        if (
            archive.GetEntry(requiredAssembly) is null
            || (
                id == "DrillPress.Cli"
                && archive.GetEntry("tools/net10.0/any/buildhost/DrillPress.BuildHost.dll") is null
            )
        )
        {
            throw new InvalidDataException($"{id} is missing a required assembly.");
        }

        foreach (var assembly in assemblies)
        {
            using var content = assembly.Open();
            using var seekable = new MemoryStream();
            content.CopyTo(seekable);
            seekable.Position = 0;
            if (assemblyVersions.Read(seekable) != version)
            {
                throw new InvalidDataException(
                    $"{id} contains a stale assembly: {assembly.FullName}."
                );
            }
        }
    }

    public static IReadOnlyDictionary<string, string> HashContents(ZipArchive archive) =>
        archive
            .Entries.Where(entry => entry.FullName != ".signature.p7s")
            .ToDictionary(
                entry => entry.FullName,
                entry =>
                {
                    using var stream = entry.Open();
                    return Convert.ToHexString(SHA256.HashData(stream));
                }
            );
}
