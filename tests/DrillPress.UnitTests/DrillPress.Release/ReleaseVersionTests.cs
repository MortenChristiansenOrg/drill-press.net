using DrillPress.Release;
using Xunit;

namespace DrillPress.UnitTests.Release;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v0.0.1", "0.0.1", false)]
    [InlineData("v1.2.3", "1.2.3", false)]
    [InlineData("v1.0.0-rc.1", "1.0.0-rc.1", true)]
    [InlineData("v1.0.0-alpha-beta.0", "1.0.0-alpha-beta.0", true)]
    public void Derives_full_package_version(string tag, string expected, bool prerelease)
    {
        var version = ReleaseVersion.FromTag(tag);

        Assert.Equal((expected, prerelease), (version.Version, version.IsPrerelease));
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("v1.2")]
    [InlineData("v01.2.3")]
    [InlineData("v1.2.3-01")]
    [InlineData("v1.2.3-rc..1")]
    [InlineData("v1.2.3+metadata")]
    [InlineData("v1.2.3\n")]
    [InlineData("v1.2.3-rc_1")]
    [InlineData("v65535.0.0")]
    [InlineData("v99999999999999.0.0")]
    public void Rejects_invalid_or_unrepresentable_versions(string tag)
    {
        Assert.Throws<InvalidDataException>(() => ReleaseVersion.FromTag(tag));
    }
}
