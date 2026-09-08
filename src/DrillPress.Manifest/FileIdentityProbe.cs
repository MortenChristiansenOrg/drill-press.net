using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DrillPress.Manifest;

/// <summary>Reads physical identity through the OS boundary; file-policy tests replace this probe with a fake.</summary>
public partial class FileIdentityProbe
{
    /// <summary>Returns metadata for an absolute target path, following aliases; null means identity could not be established.</summary>
    public virtual PhysicalFileIdentity? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            return OperatingSystem.IsWindows() ? ReadWindows(path) : OperatingSystem.IsLinux() ? ReadLinux(path) : null;
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    private static unsafe PhysicalFileIdentity? ReadLinux(string path)
    {
        const uint requested = 0x105; // STATX_TYPE | STATX_NLINK | STATX_INO.
        byte* buffer = stackalloc byte[256];
        if (Statx(-100, path, 0, requested, buffer) != 0)
        {
            return null;
        }

        var data = new ReadOnlySpan<byte>(buffer, 256);
        if ((BinaryPrimitives.ReadUInt32LittleEndian(data) & requested) != requested)
        {
            return null;
        }

        var key = Convert.ToHexString(data.Slice(136, 8)) + ":" + Convert.ToHexString(data.Slice(32, 8));
        return new(key, BinaryPrimitives.ReadUInt32LittleEndian(data[16..]),
            (BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0xF000) == 0x8000);
    }

    private static unsafe PhysicalFileIdentity? ReadWindows(string path)
    {
        var extended = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : path.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        using var handle = OpenFile(extended, 0, 7, 0, 3, 0, 0);
        byte* identity = stackalloc byte[24];
        byte* standard = stackalloc byte[24];
        if (handle.IsInvalid || GetInformation(handle, 18, identity, 24) == 0 || GetInformation(handle, 1, standard, 24) == 0)
        {
            return null;
        }

        var data = new ReadOnlySpan<byte>(standard, 24);
        return new(Convert.ToHexString(new ReadOnlySpan<byte>(identity, 24)),
            BinaryPrimitives.ReadUInt32LittleEndian(data[16..]), data[21] == 0 && data[20] == 0);
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial int Statx(int directory, string path, int flags, uint mask, byte* buffer);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle OpenFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    private static unsafe partial int GetInformation(SafeFileHandle handle, int informationClass, byte* information, uint size);
}
