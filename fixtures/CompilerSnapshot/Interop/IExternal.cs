using System.Runtime.InteropServices;

[assembly: Guid("8EE2B3E8-CC3F-4FA8-821C-A5398D0A3E63")]
[assembly: ImportedFromTypeLib("CompilerSnapshotFixture")]

namespace External;

[
    ComImport,
    Guid("403F96EE-62C7-4A50-9B0D-72A4176B8C89"),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown)
]
public interface IExternal
{
    void Execute();
}
