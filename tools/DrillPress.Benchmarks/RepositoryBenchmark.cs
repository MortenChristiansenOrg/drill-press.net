using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text.Json;
using DrillPress.Conformance;
using DrillPress.Manifest;

namespace DrillPress.Benchmarks;

public sealed class RepositoryBenchmark(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly List<RepositoryRun> _runs = [];

    public async Task RunAsync(string checkout, string output, int repetitions, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(repetitions, 1);
        if (RuntimeInformation.OSArchitecture != Architecture.X64 || !(OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
        {
            throw new PlatformNotSupportedException("Repository measurements support linux-x64 and win-x64.");
        }

        var root = _fileSystem.Directory.GetCurrentDirectory();
        checkout = _fileSystem.Path.GetFullPath(checkout);
        output = _fileSystem.Path.GetFullPath(output);
        if (_fileSystem.Directory.Exists(output) || _fileSystem.File.Exists(output))
        {
            throw new IOException($"Report destination already exists: {output}");
        }

        var outputRelative = _fileSystem.Path.GetRelativePath(checkout, output);
        if (outputRelative != ".." && !outputRelative.StartsWith(".." + _fileSystem.Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !_fileSystem.Path.IsPathRooted(outputRelative))
        {
            throw new IOException("Reports must be outside the shared benchmark checkout.");
        }

        _fileSystem.Directory.CreateDirectory(output);
        var measurements = new RepositoryMeasurements(_fileSystem, root, output);
        try
        {
            var target = await new PinnedXunit(_fileSystem).PrepareAsync(checkout, At("preparation.json"), cancellationToken);
            var sdk = await measurements.DotnetAsync("sdk", ["--info"], cancellationToken);
            var revision = await measurements.RunAsync("tool-revision", "git", ["rev-parse", "HEAD"], root, false, [], cancellationToken);
            await WriteAsync(At("environment.json"), new
            {
                xunitRevision = PinnedXunit.Revision, toolRevision = await _fileSystem.File.ReadAllTextAsync(revision.StandardOutputPath, cancellationToken),
                sdk = await _fileSystem.File.ReadAllTextAsync(sdk.StandardOutputPath, cancellationToken),
                platform = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processorCount = Environment.ProcessorCount, runtime = RuntimeInformation.FrameworkDescription,
                capturedAtUtc = DateTimeOffset.UtcNow, repetitions,
                methodology = "Serial fresh processes. First and repeated observations after restore/publish; OS caches are not flushed. No cold-filesystem claim. Persistent MSBuild servers are not included in process CPU/RSS. Fix runs each use a fresh full copy, restored before initial signature verification.",
                exportOptions = Array.Empty<string>(), variants = new[] { "managed-exhaustive", "managed-optimized", "native-exhaustive", "native-optimized" },
            }, cancellationToken);
            await new RepositoryIdentity(_fileSystem).WriteAsync(measurements, root, output, cancellationToken);
            await PrepareToolsAsync(measurements, output, cancellationToken);
            RepositoryWorkload[] workloads =
            [
                new("sample", _fileSystem.Path.Combine(root, "Sample Solution"), _fileSystem.Path.Combine(root, "Sample Solution", "DrillPress.SampleTarget.slnx")),
                new("compiler", _fileSystem.Path.Combine(root, "fixtures", "CompilerSnapshot"), _fileSystem.Path.Combine(root, "fixtures", "CompilerSnapshot", "Selected.slnx")),
                new("xunit", checkout, target),
            ];
            foreach (var workload in workloads)
            {
                await MeasureWorkloadAsync(measurements, workload, Variants(output), root, output, repetitions, cancellationToken);
            }

            await WriteAsync(At("report.json"), new { complete = true, runs = _runs, operations = measurements.Operations }, cancellationToken);
        }
        catch (Exception exception)
        {
            await WriteAsync(At("report.json"), new { complete = false, error = exception.ToString(), runs = _runs, operations = measurements.Operations }, CancellationToken.None);
            throw;
        }

        string At(string name) => _fileSystem.Path.Combine(output, name);
    }

    private async Task PrepareToolsAsync(RepositoryMeasurements measurements, string output, CancellationToken cancellationToken)
    {
        await measurements.DotnetAsync("build", ["build", "DrillPress.slnx", "-c", "Release"], cancellationToken);
        foreach (var project in new[] { "Generator", "Interop" })
        {
            await measurements.DotnetAsync("build-" + project, ["build", $"fixtures/CompilerSnapshot/{project}/{project}.csproj", "-c", "Release"], cancellationToken);
        }

        await measurements.DotnetAsync("restore-compiler", ["restore", "fixtures/CompilerSnapshot/Selected.slnx"], cancellationToken);
        await measurements.DotnetAsync("restore-sample", ["restore", "Sample Solution/DrillPress.SampleTarget.slnx"], cancellationToken);
        const string sample = "samples/DrillPress.SampleRules/DrillPress.SampleRules.csproj";
        await measurements.DotnetAsync("publish-managed", ["publish", sample, "-c", "Release", "-p:PublishAot=false", "--self-contained", "false", "-o", _fileSystem.Path.Combine(output, "managed")], cancellationToken);
        var rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        var publication = await measurements.DotnetAsync("publish-native", ["publish", sample, "-c", "Release", "-r", rid, "-o", _fileSystem.Path.Combine(output, "native")], cancellationToken);
        DrillPress.BundleVerification.PublishWarnings.Validate(await _fileSystem.File.ReadAllTextAsync(publication.StandardOutputPath, cancellationToken) +
            await _fileSystem.File.ReadAllTextAsync(publication.StandardErrorPath, cancellationToken));
        await WriteAsync(_fileSystem.Path.Combine(output, "bundles.json"), Variants(output).DistinctBy(variant => variant.Bundle)
            .Select(variant => new PublishedBundleIdentity(_fileSystem).Read(variant.Bundle)).ToArray(), cancellationToken);
    }

    private RepositoryVariant[] Variants(string output)
    {
        var managed = _fileSystem.Path.Combine(output, "managed", "DrillPress.SampleRules.dll");
        var native = _fileSystem.Path.Combine(output, "native", OperatingSystem.IsWindows() ? "DrillPress.SampleRules.exe" : "DrillPress.SampleRules");
        return [new("managed-exhaustive", managed, false, false), new("managed-optimized", managed, false, true),
            new("native-exhaustive", native, true, false), new("native-optimized", native, true, true)];
    }

    private async Task MeasureWorkloadAsync(RepositoryMeasurements measurements, RepositoryWorkload workload, RepositoryVariant[] variants,
        string root, string output, int repetitions, CancellationToken cancellationToken)
    {
        var signatures = new ResultSignatures(_fileSystem);
        ResultSignatureFiles? baseline = null;
        ResultSignatureFiles? recheckedBaseline = null;
        string? inputHash = null;
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            var name = workload.Name + "/run-" + repetition;
            var snapshot = _fileSystem.Path.Combine(output, name, "snapshot.json");
            await ExportAsync(measurements, name + "/export", workload.Target, snapshot, root, cancellationToken);
            var input = await new CompilationSnapshotFile(_fileSystem).ReadAsync(snapshot, cancellationToken);
            var currentInputHash = SignatureNormalization.Hash(new SignatureNormalization(_fileSystem, input, workload.Root).Snapshot(input));
            inputHash ??= currentInputHash;
            if (inputHash != currentInputHash)
            {
                throw new InvalidDataException("Benchmark inputs changed between repetitions.");
            }

            foreach (var variant in variants)
            {
                var prefix = name + "/" + variant.Name;
                var response = await EvaluateAsync(measurements, prefix + "/rules", variant, snapshot, root, cancellationToken);
                var signature = await signatures.WriteAsync(snapshot, response.StandardOutputPath, _fileSystem.Path.Combine(output, prefix, "initial"), cancellationToken, workload.Root);
                baseline ??= signature;
                ResultSignatures.RequireEqual(baseline, signature);
                var check = await CliAsync(measurements, prefix + "/check", "check", variant, workload.Target, root, cancellationToken);
                RequireProfileTotals(measurements.Operations.Last().Profile, 1);
                await RequirePublicAsync(snapshot, response.StandardOutputPath, check.StandardOutputPath, cancellationToken);
                ResultSignatureFiles? rechecked = null;
                if (workload.Name == "xunit")
                {
                    rechecked = await new RepositoryFixBenchmark(_fileSystem).RunAsync(measurements, workload, variant, snapshot, signature,
                        root, output, prefix, cancellationToken);
                    recheckedBaseline ??= rechecked;
                    ResultSignatures.RequireEqual(recheckedBaseline, rechecked);
                }

                _runs.Add(new(workload.Name, repetition, variant.Name, _fileSystem.FileInfo.New(snapshot).Length, signature, rechecked));
                await WriteAsync(_fileSystem.Path.Combine(output, "runs.json"), _runs, cancellationToken);
            }
        }
    }

    internal static async Task RequirePublicAsync(IFileSystem fileSystem, string snapshotPath, string responsePath, string publicPath, CancellationToken cancellationToken)
    {
        var snapshot = await new CompilationSnapshotFile(fileSystem).ReadAsync(snapshotPath, cancellationToken);
        var plan = BundleResponseProtocol.Read(await fileSystem.File.ReadAllTextAsync(responsePath, cancellationToken), snapshot);
        var expected = new CompactDiagnosticRenderer(fileSystem).Render(plan);
        var actual = await fileSystem.File.ReadAllTextAsync(publicPath, cancellationToken);
        if (expected != actual)
        {
            throw new InvalidDataException($"CLI output differs from the saved complete result: {publicPath}");
        }
    }

    private Task RequirePublicAsync(string snapshot, string response, string publicOutput, CancellationToken cancellationToken) =>
        RequirePublicAsync(_fileSystem, snapshot, response, publicOutput, cancellationToken);

    internal static void RequireProfileTotals(ProfileEvent[] profile, int evaluations)
    {
        if (profile.Count(entry => entry.Component == "build-host" && entry.Phase == "total") != evaluations ||
            profile.Count(entry => entry.Component == "rules" && entry.Phase == "total") != evaluations ||
            profile.Count(entry => entry.Component == "cli" && entry.Phase == "total") != 1)
        {
            throw new InvalidDataException("The CLI profile is missing an export, evaluation, or total measurement.");
        }
    }

    internal static Task<MeasuredExecution> ExportAsync(RepositoryMeasurements measurements, string name, string target, string snapshot,
        string root, CancellationToken cancellationToken) => measurements.RunAsync(name, "dotnet",
            [Tool(root, "BuildHost"), "export", target, snapshot, "--profile"], root, false, ["build-host"], cancellationToken);

    internal static Task<MeasuredExecution> EvaluateAsync(RepositoryMeasurements measurements, string name, RepositoryVariant variant,
        string snapshot, string root, CancellationToken cancellationToken)
    {
        string[] arguments = ["check", snapshot, "--profile", .. variant.Optimized ? Array.Empty<string>() : ["--no-optimization"]];
        return measurements.RunAsync(name, variant.Native ? variant.Bundle : "dotnet", variant.Native ? arguments : [variant.Bundle, .. arguments],
            root, true, ["rules"], cancellationToken);
    }

    internal static Task<MeasuredExecution> CliAsync(RepositoryMeasurements measurements, string name, string command, RepositoryVariant variant,
        string target, string root, CancellationToken cancellationToken) => measurements.RunAsync(name, "dotnet",
            [Tool(root, "Cli"), command, "--build-host", Tool(root, "BuildHost"), "--rules", variant.Bundle, "--profile",
                .. variant.Optimized ? Array.Empty<string>() : ["--no-optimization"], target], root, true, ["cli", "build-host", "rules"], cancellationToken);

    private static string Tool(string root, string name) => System.IO.Path.Combine(root, "src", "DrillPress." + name, "bin", "Release", "net10.0", "DrillPress." + name + ".dll");

    private Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken) =>
        _fileSystem.File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
}
