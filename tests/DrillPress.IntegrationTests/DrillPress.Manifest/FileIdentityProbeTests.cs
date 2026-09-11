using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.IntegrationTests.Manifest;

public sealed class FileIdentityProbeTests : IntegrationTest
{
    [Fact]
    public void Ordinary_file_identity_is_stable_and_different_files_have_different_keys()
    {
        var directory = CreateTemporaryDirectory("drillpress-identity-");
        var first = FileSystem.Path.Combine(directory.FullName, "A.cs");
        var second = FileSystem.Path.Combine(directory.FullName, "B.cs");
        FileSystem.File.WriteAllText(first, "A");
        FileSystem.File.WriteAllText(second, "B");
        var probe = new FileIdentityProbe();

        var identity = probe.Read(first);
        var repeated = probe.Read(first);
        var other = probe.Read(second);

        Assert.NotNull(identity);
        Assert.Equal(identity, repeated);
        Assert.NotNull(other);
        Assert.NotEqual(identity.Key, other.Key);
        Assert.Equal(1u, identity.LinkCount);
        Assert.True(identity.IsRegularFile);
        Assert.True(new SourceFilePolicy().Inspect(first).IsEditable);
    }

    [Fact]
    public async Task Hard_link_aliases_share_identity_and_are_not_editable()
    {
        var directory = CreateTemporaryDirectory("drillpress-hardlinks-");
        var first = FileSystem.Path.Combine(directory.FullName, "A.cs");
        var second = FileSystem.Path.Combine(directory.FullName, "B.cs");
        FileSystem.File.WriteAllText(first, "A");
        await CreateHardLinkAsync(first, second);
        var probe = new FileIdentityProbe();

        var identity = probe.Read(first);
        var alias = probe.Read(second);

        Assert.NotNull(identity);
        Assert.Equal(identity, alias);
        Assert.Equal(2u, identity.LinkCount);
        Assert.False(new SourceFilePolicy().Inspect(first).IsEditable);
        Assert.False(new SourceFilePolicy().Inspect(second).IsEditable);
    }

    private async Task CreateHardLinkAsync(string source, string target)
    {
        var command = OperatingSystem.IsWindows() ? "fsutil" : "ln";
        string[] arguments = OperatingSystem.IsWindows()
            ? ["hardlink", "create", target, source]
            : [source, target];
        var result = await RunProcessAsync(
            command,
            arguments,
            FileSystem.Path.GetDirectoryName(source)!,
            TestContext.Current.CancellationToken
        );
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
    }
}
