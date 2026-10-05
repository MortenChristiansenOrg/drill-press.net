using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace DrillPress.Engine;

internal sealed class CoverageCache(IFileSystem fileSystem, string? root = null)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    internal string Root { get; } =
        fileSystem.Path.GetFullPath(
            root
                ?? fileSystem.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DrillPress",
                    "coverage",
                    CoverageTool.Version
                )
        );

    internal string ReportPath(string identity) =>
        _fileSystem.Path.Combine(Root, identity + ".xml");

    internal Task<Stream> AcquireAsync(string sourceRoot, CancellationToken cancellationToken) =>
        LockAsync(
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        OperatingSystem.IsWindows() ? sourceRoot.ToUpperInvariant() : sourceRoot
                    )
                )
            ) + ".collection.lock",
            cancellationToken
        );

    internal Task<Stream> AcquireToolAsync(CancellationToken cancellationToken) =>
        LockAsync("tool.lock", cancellationToken);

    private async Task<Stream> LockAsync(string name, CancellationToken cancellationToken)
    {
        _fileSystem.Directory.CreateDirectory(Root);
        var path = _fileSystem.Path.Combine(Root, name);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return _fileSystem.File.Open(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException exception) when ((exception.HResult & 0xFFFF) is 11 or 32 or 33)
            {
                await Task.Delay(200, cancellationToken);
            }
        }
    }
}
