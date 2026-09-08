using System.IO.Abstractions;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DrillPress.Cli;

internal class SnapshotDirectoryPermissions(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    internal virtual void Restrict(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            RestrictWindows(directory);
        }
    }

    [SupportedOSPlatform("windows")]
    private void RestrictWindows(string directory)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new IOException("Cannot identify the snapshot owner.");
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        _fileSystem.Directory.SetAccessControl(directory, security);
    }
}
