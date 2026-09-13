using System.IO.Abstractions;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

public sealed class CliSnapshotPermissionsTests : IntegrationTest
{
    public static bool IsLinux => OperatingSystem.IsLinux();
    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact(SkipUnless = nameof(IsLinux), Skip = "Requires Linux file modes.")]
    [SupportedOSPlatform("linux")]
    public async Task Unix_snapshot_and_directory_are_owner_only_until_cleanup()
    {
        using var fixture = new SnapshotPermissionFixture();

        var permissions = await fixture.CaptureAsync(
            (directory, snapshot) =>
                new[]
                {
                    FileSystem.File.GetUnixFileMode(directory),
                    FileSystem.File.GetUnixFileMode(snapshot),
                }
        );

        Assert.Equal(
            [
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
            ],
            permissions
        );
        Assert.True(fixture.ChildExited);
        Assert.True(fixture.DirectoryRemoved);
    }

    [Fact(SkipUnless = nameof(IsWindows), Skip = "Requires Windows ACLs.")]
    [SupportedOSPlatform("windows")]
    public async Task Windows_snapshot_inherits_only_the_current_users_protected_directory_access()
    {
        using var fixture = new SnapshotPermissionFixture();
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User!.Value;

        var permissions = await fixture.CaptureAsync(ReadWindowsPermissions);

        Assert.True(permissions.Protected);
        Assert.Equal(owner, permissions.Owner);
        Assert.Equal(
            new(
                owner,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow,
                false
            ),
            Assert.Single(permissions.DirectoryRules)
        );
        Assert.Equal(
            new(
                owner,
                FileSystemRights.FullControl,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow,
                true
            ),
            Assert.Single(permissions.FileRules)
        );
        Assert.True(fixture.ChildExited);
        Assert.True(fixture.DirectoryRemoved);
    }

    [SupportedOSPlatform("windows")]
    private static WindowsSnapshotPermissions ReadWindowsPermissions(
        string directory,
        string snapshot
    )
    {
        var security = FileSystem.Directory.GetAccessControl(directory);
        return new(
            security.AreAccessRulesProtected,
            security.GetOwner(typeof(SecurityIdentifier))!.Value,
            Rules(security),
            Rules(FileSystem.File.GetAccessControl(snapshot))
        );
    }

    [SupportedOSPlatform("windows")]
    private static SnapshotAccessRule[] Rules(FileSystemSecurity security) =>
        security
            .GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => new SnapshotAccessRule(
                rule.IdentityReference.Value,
                rule.FileSystemRights,
                rule.InheritanceFlags,
                rule.PropagationFlags,
                rule.AccessControlType,
                rule.IsInherited
            ))
            .ToArray();
}
