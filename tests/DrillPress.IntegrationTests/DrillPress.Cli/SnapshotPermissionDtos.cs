using System.Security.AccessControl;

namespace DrillPress.IntegrationTests.DrillPress.Cli;

internal sealed record WindowsSnapshotPermissions(
    bool Protected,
    string Owner,
    SnapshotAccessRule[] DirectoryRules,
    SnapshotAccessRule[] FileRules
);

internal sealed record SnapshotAccessRule(
    string Identity,
    FileSystemRights Rights,
    InheritanceFlags Inheritance,
    PropagationFlags Propagation,
    AccessControlType Access,
    bool Inherited
);
