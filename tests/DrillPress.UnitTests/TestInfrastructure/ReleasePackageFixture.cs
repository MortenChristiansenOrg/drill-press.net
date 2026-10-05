using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Text;
using DrillPress.Release;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class ReleasePackageFixture
{
    public MockFileSystem FileSystem { get; } = new();
    public string Directory => FileSystem.Path.GetFullPath("feed");
    public ReleaseVersion Version { get; } = ReleaseVersion.FromTag("v1.0.0-rc.1");
    public string AssemblyVersion { get; set; } = "1.0.0-rc.1";

    public ReleasePackageFixture()
    {
        foreach (var id in PackageInventory.PackageIds)
        {
            AddPackage(id);
        }
    }

    public void AddPackage(
        string id,
        string version = "1.0.0-rc.1",
        string dependencyVersion = "[1.0.0-rc.1]",
        bool buildHost = true,
        bool dependencies = true
    )
    {
        string[] dependencyIds = id switch
        {
            "DrillPress.Engine" => ["DrillPress.Manifest", "DrillPress.RuleAuthoring"],
            "DrillPress.RuleAuthoring" => ["DrillPress.Manifest"],
            "DrillPress.Linq" => ["DrillPress.RuleAuthoring"],
            "DrillPress.Testing" => ["DrillPress.Engine"],
            _ => [],
        };
        var dependencyXml = dependencies
            ? string.Concat(
                dependencyIds.Select(dependency =>
                    $"<dependency id=\"{dependency}\" version=\"{dependencyVersion}\" />"
                )
            )
            : "";
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(
                archive,
                id + ".nuspec",
                $"<package><metadata><id>{id}</id><version>{version}</version><dependencies>{dependencyXml}</dependencies></metadata></package>"
            );
            var prefix = id == "DrillPress.Cli" ? "tools/net10.0/any/" : "lib/net10.0/";
            Write(archive, prefix + id + ".dll", "assembly");
            if (id == "DrillPress.Cli" && buildHost)
            {
                Write(archive, prefix + "buildhost/DrillPress.BuildHost.dll", "assembly");
            }
        }

        FileSystem.AddFile(
            FileSystem.Path.Combine(Directory, id + ".nupkg"),
            new MockFileData(buffer.ToArray())
        );
    }

    public PackageInventory CreateInventory() =>
        new(FileSystem, new StubAssemblyVersionReader(this));

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}
