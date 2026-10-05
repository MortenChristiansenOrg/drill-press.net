using System.IO.Abstractions;
using System.Security.Cryptography;

namespace DrillPress.Engine;

internal sealed class CoverageBinaryReader(
    IFileSystem fileSystem,
    CoverageProcess process,
    CoverageCache cache
)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly CoverageProcess _process = process;
    private readonly CoverageCache _cache = cache;

    internal async Task EnrichAsync(
        string binary,
        string xml,
        string root,
        CancellationToken cancellationToken
    )
    {
        var executable = await ExtractAsync(cancellationToken);
        await _process.RunAsync(
            "dotnet",
            [executable, binary, xml, CoverageTool.Version],
            root,
            cancellationToken
        );
    }

    private async Task<string> ExtractAsync(CancellationToken cancellationToken)
    {
        const string prefix = "DrillPress.CoverageReader/";
        var assembly = typeof(CoverageBinaryReader).Assembly;
        var resources = assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix))
            .Order()
            .ToArray();
        if (resources.Length == 0)
            throw new InvalidOperationException("The packaged coverage reader is missing.");
        var files = await ReadFilesAsync(assembly, resources, prefix, cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(file.Path + "\0"));
            hash.AppendData(file.Bytes);
        }
        var directory = _fileSystem.Path.Combine(
            _cache.Root,
            "reader",
            Convert.ToHexString(hash.GetHashAndReset())
        );
        return await WriteFilesAsync(directory, files, cancellationToken);
    }

    private async Task<string> WriteFilesAsync(
        string directory,
        List<(string Path, byte[] Bytes)> files,
        CancellationToken cancellationToken
    )
    {
        var executable = _fileSystem.Path.Combine(directory, "DrillPress.CoverageReader.dll");
        var marker = _fileSystem.Path.Combine(directory, ".complete");
        using var readerLock = await _cache.AcquireToolAsync(cancellationToken);
        if (!_fileSystem.File.Exists(marker))
        {
            foreach (var file in files)
            {
                var destination = _fileSystem.Path.Combine(directory, file.Path);
                _fileSystem.Directory.CreateDirectory(
                    _fileSystem.Path.GetDirectoryName(destination)!
                );
                await _fileSystem.File.WriteAllBytesAsync(
                    destination,
                    file.Bytes,
                    cancellationToken
                );
            }
            await _fileSystem.File.WriteAllTextAsync(marker, "", cancellationToken);
        }
        return executable;
    }

    private async Task<List<(string Path, byte[] Bytes)>> ReadFilesAsync(
        System.Reflection.Assembly assembly,
        string[] resources,
        string prefix,
        CancellationToken cancellationToken
    )
    {
        var files = new List<(string Path, byte[] Bytes)>();
        foreach (var name in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, cancellationToken);
            files.Add((name[prefix.Length..], copy.ToArray()));
        }
        const string core = "Microsoft.CodeCoverage.Core.dll";
        var collector = _fileSystem.Path.Combine(
            _cache.Root,
            "tool",
            ".store",
            "dotnet-coverage",
            CoverageTool.Version,
            "dotnet-coverage",
            CoverageTool.Version,
            "tools",
            "net8.0",
            "any",
            core
        );
        files.Add((core, await _fileSystem.File.ReadAllBytesAsync(collector, cancellationToken)));
        return files;
    }
}
