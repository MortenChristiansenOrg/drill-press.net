using System.Diagnostics;
using System.IO.Compression;
using System.Net;

namespace DrillPress.Release;

public class NugetFeed(HttpClient client)
{
    public virtual async Task<IReadOnlyDictionary<string, string>?> ReadAsync(
        ReleasePackage package,
        CancellationToken cancellationToken
    )
    {
        var id = package.Id.ToLowerInvariant();
        var version = package.Version.ToLowerInvariant();
        using var response = await client.GetAsync(
            $"https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.{version}.nupkg",
            cancellationToken
        );
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var stream = new MemoryStream(
            await response.Content.ReadAsByteArrayAsync(cancellationToken)
        );
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return PackageInventory.HashContents(archive);
    }

    public virtual async Task PushAsync(
        ReleasePackage package,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (
            var argument in new[]
            {
                "nuget",
                "push",
                package.Path,
                "--source",
                "https://api.nuget.org/v3/index.json",
                "--api-key",
                apiKey,
                "--skip-duplicate",
            }
        )
        {
            start.ArgumentList.Add(argument);
        }

        using var process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Cannot start dotnet nuget push.");
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
        });
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(output, error);
        if (process.ExitCode != 0)
        {
            // Do not echo a child-process diagnostic that might contain the credential.
            throw new InvalidOperationException(
                $"NuGet push failed for {package.Id} (exit {process.ExitCode}). Rerun after checking feed permissions and connectivity."
            );
        }
    }
}
