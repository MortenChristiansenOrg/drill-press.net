using System.IO.Pipes;
using System.Text.Json;

namespace DrillPress.Manifest;

/// <summary>Serializes requests to a private, idle-expiring compiler worker.</summary>
internal sealed class CommandWorker
{
    internal const string Command = "drillpress-worker";
    internal const string StopCommand = "drillpress-worker-stop";
    internal const int MaximumFrameBytes = 80 * 1024 * 1024;

    internal async Task RunAsync(
        string name,
        Func<string[], CancellationToken, Task<WorkerResponse>> execute,
        CancellationToken cancellationToken
    )
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var pipe = new NamedPipeServerStream(
            name,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
        while (!lifetime.IsCancellationRequested)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            idle.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                await pipe.WaitForConnectionAsync(idle.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return;
            }

            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            try
            {
                var bytes = await ReadAsync(pipe, request.Token);
                var arguments =
                    JsonSerializer.Deserialize(bytes, WorkerJsonContext.Default.StringArray)
                    ?? throw new InvalidDataException("Missing worker arguments.");
                if (arguments is [StopCommand])
                {
                    await WriteAsync(
                        pipe,
                        JsonSerializer.SerializeToUtf8Bytes(
                            new WorkerResponse(0, "", ""),
                            WorkerJsonContext.Default.WorkerResponse
                        ),
                        request.Token
                    );
                    return;
                }
                var disconnected = pipe.ReadAsync(new byte[1], request.Token).AsTask();
                var executing = Task.Run(
                    () => execute(arguments, lifetime.Token),
                    CancellationToken.None
                );
                if (await Task.WhenAny(executing, disconnected) == disconnected)
                {
                    await lifetime.CancelAsync();
                    await ((Task)executing.WaitAsync(TimeSpan.FromSeconds(1))).ConfigureAwait(
                        ConfigureAwaitOptions.SuppressThrowing
                    );
                    return;
                }
                var result = await executing;
                await WriteAsync(
                    pipe,
                    JsonSerializer.SerializeToUtf8Bytes(
                        result,
                        WorkerJsonContext.Default.WorkerResponse
                    ),
                    request.Token
                );
                await request.CancelAsync();
                await ((Task)disconnected).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                pipe.Disconnect();
            }
            catch (Exception exception)
                when (exception is IOException or OperationCanceledException)
            {
                await request.CancelAsync();
                await lifetime.CancelAsync();
            }
        }
    }

    internal static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaximumFrameBytes)
            throw new InvalidDataException("Worker frame exceeds its capture limit.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }

    internal static async Task WriteAsync(
        Stream stream,
        byte[] bytes,
        CancellationToken cancellationToken
    )
    {
        if (bytes.Length > MaximumFrameBytes)
            throw new InvalidDataException("Worker frame exceeds its capture limit.");
        var header = new byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
