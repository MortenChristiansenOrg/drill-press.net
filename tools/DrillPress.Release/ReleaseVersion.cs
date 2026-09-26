using System.Text.RegularExpressions;

namespace DrillPress.Release;

public sealed partial class ReleaseVersion
{
    private ReleaseVersion(string version)
    {
        Version = version;
    }

    public string Version { get; }
    public bool IsPrerelease => Version.Contains('-');

    public static ReleaseVersion FromTag(string tag)
    {
        if (!TagPattern().IsMatch(tag))
        {
            throw new InvalidDataException(
                "Expected vMAJOR.MINOR.PATCH[-PRERELEASE], without leading zeroes or build metadata."
            );
        }

        var version = tag[1..];
        if (
            version
                .Split('-')[0]
                .Split('.')
                .Any(part => !ushort.TryParse(part, out var value) || value == ushort.MaxValue)
        )
        {
            throw new InvalidDataException(
                "Version components must fit assembly metadata (0–65534)."
            );
        }

        return new ReleaseVersion(version);
    }

    [GeneratedRegex(
        @"\Av(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-((0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(\.(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?\z"
    )]
    private static partial Regex TagPattern();
}
