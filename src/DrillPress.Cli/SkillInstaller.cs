using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.Cli;

/// <summary>Writes the rule-authoring agent skill embedded in this tool version into a skills directory.</summary>
internal sealed class SkillInstaller(IFileSystem fileSystem)
{
    internal const string Name = "drillpress-rules";
    internal const string DefaultDirectory = ".claude/skills";
    private const string ResourcePrefix = "skill/";
    private const string VersionPlaceholder = "{{version}}";

    /// <summary>Overwrites the bundled files under <c>skillsDirectory/drillpress-rules</c> and returns that directory; other files there are kept.</summary>
    internal async Task<string> InstallAsync(
        string skillsDirectory,
        CancellationToken cancellationToken
    )
    {
        var path = fileSystem.Path;
        var target = path.GetFullPath(path.Combine(skillsDirectory, Name));
        var assembly = typeof(SkillInstaller).Assembly;
        foreach (
            var resource in assembly
                .GetManifestResourceNames()
                .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
        )
        {
            var relative = resource[ResourcePrefix.Length..].Replace('\\', '/');
            var destination = path.Combine([target, .. relative.Split('/')]);
            fileSystem.Directory.CreateDirectory(path.GetDirectoryName(destination)!);
            await using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync(cancellationToken);
            await fileSystem.File.WriteAllTextAsync(
                destination,
                text.Replace(VersionPlaceholder, ComponentVersion.Current),
                cancellationToken
            );
        }
        return target;
    }
}
