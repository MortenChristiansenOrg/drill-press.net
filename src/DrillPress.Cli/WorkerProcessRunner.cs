using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DrillPress.Manifest;

namespace DrillPress.Cli;

internal sealed class WorkerProcessRunner(IFileSystem fileSystem) : ChildProcessRunner
{
    internal const string Prefix = "--persistent-worker=";
    private readonly IFileSystem _fileSystem = fileSystem;

    public override async Task<ChildProcessResult> CaptureAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        if (
            arguments.FirstOrDefault(argument =>
                argument.StartsWith(Prefix, StringComparison.Ordinal)
            )
            is not { } marker
        )
            return await base.CaptureAsync(executable, arguments, cancellationToken);
        var path = _fileSystem.Path.GetFullPath(executable);
        var fingerprint = Fingerprint(path);
        var key =
            path
            + "\n"
            + fingerprint
            + "\n"
            + _fileSystem.Directory.GetCurrentDirectory()
            + "\n"
            + marker;
        var name =
            "drillpress-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..40];
        using var pipe = new NamedPipeClientStream(
            ".",
            name,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
        try
        {
            await pipe.ConnectAsync(100, cancellationToken);
        }
        catch (TimeoutException)
        {
            if (arguments.Contains(CommandWorker.StopCommand))
                return new(0, "", "");
            var start = CreateStartInfo(path, [CommandWorker.Command, name]);
            start.RedirectStandardInput = false;
            using var process =
                Process.Start(start)
                ?? throw new IOException("Could not start the compiler worker.");
            await pipe.ConnectAsync(30_000, cancellationToken);
        }

        var request = arguments.Where(argument => argument != marker).ToArray();
        await CommandWorker.WriteAsync(
            pipe,
            JsonSerializer.SerializeToUtf8Bytes(request, WorkerJsonContext.Default.StringArray),
            cancellationToken
        );
        var bytes = await CommandWorker.ReadAsync(pipe, cancellationToken);
        var result =
            JsonSerializer.Deserialize(bytes, WorkerJsonContext.Default.WorkerResponse)
            ?? throw new InvalidDataException("Missing worker response.");
        return new(result.ExitCode, result.StandardOutput, result.StandardError);
    }

    private string Fingerprint(string executable)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var directory = _fileSystem.Path.GetDirectoryName(executable)!;
        foreach (
            var path in _fileSystem
                .Directory.EnumerateFiles(directory)
                .Where(path =>
                    path == executable || _fileSystem.Path.GetExtension(path) is ".dll" or ".json"
                )
                .Order(StringComparer.Ordinal)
        )
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            using var input = _fileSystem.File.OpenRead(path);
            hash.AppendData(SHA256.HashData(input));
        }
        foreach (
            System.Collections.DictionaryEntry variable in Environment
                .GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .Where(variable => variable.Key.ToString() != "CODEX_TOOL_CALL_ID")
                .OrderBy(variable => variable.Key.ToString(), StringComparer.Ordinal)
        )
            hash.AppendData(Encoding.UTF8.GetBytes(variable.Key + "=" + variable.Value + "\n"));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
