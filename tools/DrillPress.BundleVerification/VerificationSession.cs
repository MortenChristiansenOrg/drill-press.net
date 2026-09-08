using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text;
using DrillPress.Manifest;

namespace DrillPress.BundleVerification;

public sealed class VerificationSession : IDisposable
{
    private readonly IFileSystem _fileSystem;
    private readonly IDirectoryInfo _fixture;
    private BundleCase[] _contextCases = [];

    private VerificationSession(IFileSystem fileSystem, string root, string output, string rid)
    {
        _fileSystem = fileSystem;
        _fixture = fileSystem.Directory.CreateTempSubdirectory("drillpress-native-");
        RepositoryRoot = root;
        OutputDirectory = output;
        RuntimeIdentifier = rid;
    }

    public string RepositoryRoot { get; }
    public string OutputDirectory { get; }
    public string RuntimeIdentifier { get; }
    public string ManagedBundle => _fileSystem.Path.Combine(OutputDirectory, "managed", "DrillPress.SampleRules.dll");
    public string NativeBundle => _fileSystem.Path.Combine(OutputDirectory, "native",
        OperatingSystem.IsWindows() ? "DrillPress.SampleRules.exe" : "DrillPress.SampleRules");
    public byte[] PublicOutput { get; private set; } = [];

    public BundleCase[] Cases { get; private set; } = [];

    public static async Task<VerificationSession> CreateAsync(IFileSystem fileSystem, string? output = null)
    {
        var directory = fileSystem.DirectoryInfo.New(fileSystem.Directory.GetCurrentDirectory());
        while (directory is not null && !fileSystem.File.Exists(fileSystem.Path.Combine(directory.FullName, "DrillPress.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("Run from the repository directory.");
        if (RuntimeInformation.OSArchitecture != Architecture.X64 ||
            !(OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
        {
            throw new PlatformNotSupportedException("Supported runtime identifiers are linux-x64 and win-x64.");
        }

        var rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        output = fileSystem.Path.GetFullPath(output ?? fileSystem.Path.Combine(root, "artifacts", $"native-{rid}-{Guid.NewGuid():N}"));
        if (fileSystem.Directory.Exists(output) || fileSystem.File.Exists(output))
        {
            throw new IOException($"Report destination already exists: {output}");
        }

        fileSystem.Directory.CreateDirectory(output);
        var session = new VerificationSession(fileSystem, root, output, rid);
        try
        {
            await session.PublishAsync();
            await session.CreateCasesAsync();
            await session.VerifyAsync();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public async Task<ProcessOutput> ExecuteAsync(BundleMode mode, BundleCase @case)
    {
        var result = await ProcessRunner.RunAsync(
            mode == BundleMode.Managed ? "dotnet" : NativeBundle,
            mode == BundleMode.Managed ? [ManagedBundle, .. @case.Arguments] : @case.Arguments,
            RepositoryRoot);
        BundleContract.Validate(@case, result);
        return result;
    }

    public void Dispose() => _fixture.Delete(recursive: true);

    private async Task PublishAsync()
    {
        const string sample = "samples/DrillPress.SampleRules/DrillPress.SampleRules.csproj";
        await BuildCommandAsync("build.log", ["build", "DrillPress.slnx", "-c", "Release"]);
        await BuildCommandAsync("managed-publish.log",
            ["publish", sample, "-c", "Release", "-p:PublishAot=false", "--self-contained", "false", "-o", _fileSystem.Path.Combine(OutputDirectory, "managed")]);
        var publication = await BuildCommandAsync("publish.log",
            ["publish", sample, "-c", "Release", "-r", RuntimeIdentifier, "-o", _fileSystem.Path.Combine(OutputDirectory, "native")]);
        PublishWarnings.Validate(Encoding.UTF8.GetString(publication.StandardOutput) + Encoding.UTF8.GetString(publication.StandardError));
    }

    private async Task<ProcessOutput> BuildCommandAsync(string log, string[] arguments)
    {
        var result = await ProcessRunner.RunAsync("dotnet", arguments, RepositoryRoot, timeout: TimeSpan.FromMinutes(10));
        await _fileSystem.File.WriteAllBytesAsync(_fileSystem.Path.Combine(OutputDirectory, log), [.. result.StandardOutput, .. result.StandardError]);
        ProcessRunner.RequireSuccess(result);
        return result;
    }

    private string BuildHost => _fileSystem.Path.Combine(RepositoryRoot, "src/DrillPress.BuildHost/bin/Release/net10.0/DrillPress.BuildHost.dll");

    private async Task CreateCasesAsync()
    {
        var project = _fileSystem.Path.Combine(_fixture.FullName, "Probe.csproj");
        var source = _fileSystem.Path.Combine(_fixture.FullName, "Probe.cs");
        await _fileSystem.File.WriteAllTextAsync(project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>14.0</LangVersion>
              </PropertyGroup>
              <ItemGroup><Compile Remove="Coverage/**/*.cs" /></ItemGroup>
            </Project>
            """);
        await BuildCommandAsync("fixture-restore.log", ["restore", project, "--nologo"]);
        var cases = new List<BundleCase>();
        foreach (var (name, expression, outcome) in new[]
                 { ("clean", "\"\"", BundleOutcome.Clean), ("violating", "Text.Empty", BundleOutcome.Findings) })
        {
            await _fileSystem.File.WriteAllTextAsync(source, $$"""
                using Text = System.String;
                public static class Probe
                {
                    public static string Value => {{expression}};
                }
                """);
            var snapshot = _fileSystem.Path.Combine(_fixture.FullName, $"{name}.json");
            ProcessRunner.RequireSuccess(await ProcessRunner.RunAsync("dotnet", [BuildHost, "export", project, snapshot], RepositoryRoot));
            var captured = await new CompilationSnapshotFile(_fileSystem).ReadAsync(snapshot);
            var context = captured.Projects.Single();
            var document = context.Documents.Single(item => item.Path == source);
            Finding[] findings = outcome == BundleOutcome.Clean ? [] :
                [new("DP1004", "Use the empty string literal \"\" instead of string.Empty.", document.DocumentId,
                    document.Text.IndexOf("Text.Empty", StringComparison.Ordinal), "Text.Empty".Length, null)];
            FixBatch[] batches = [];
            if (findings.Length > 0)
            {
                var edit = new SourceEdit(document.FileIdentity, document.Fingerprint, findings[0].Start, "Text.Empty".Length, "Text.Empty", "\"\"");
                var signature = $"{edit.FileIdentity.Length}:{edit.FileIdentity}{edit.Fingerprint}:{edit.Start}:{edit.Length}:{edit.Replacement.Length}:{edit.Replacement}";
                var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
                batches = [new FixBatch(id, [edit], [new FixValidation(context.ContextId, true)])];
                findings = [findings[0] with { BatchId = id }];
            }

            var response = new BundleResponse(1, captured.RequestId, [new(context.ContextId, true, findings)], batches);
            var stdout = BundleResponseProtocol.Serialize(response);
            PublicOutput = Encoding.UTF8.GetBytes(new CompactDiagnosticRenderer(_fileSystem).Render(new BundleResponseValidator().Validate(captured, response)));
            cases.Add(new BundleCase(name, ["check", snapshot], outcome, stdout, []));
        }

        var invalid = _fileSystem.Path.Combine(_fixture.FullName, "invalid.json");
        await _fileSystem.File.WriteAllTextAsync(invalid,
            """{"fileIdentifier":"drillpress-compilation","formatVersion":-1,"projects":[]}""");
        cases.Add(new BundleCase("invalid", ["check", invalid], BundleOutcome.Failure, [], Encoding.UTF8.GetBytes(
            "drillpress-rules: Compilation snapshot format -1 is not supported; expected 2. Use matching Drill Press components." + Environment.NewLine)));
        Cases = cases.ToArray();
        await CreateContextCasesAsync();
    }


    private async Task CreateContextCasesAsync()
    {
        var storage = new CompilationSnapshotFile(_fileSystem);
        var baseline = await storage.ReadAsync(_fileSystem.Path.Combine(_fixture.FullName, "violating.json"));
        var project = baseline.Projects.Single();
        var ordinary = project.Documents.Single(document => document.Path.EndsWith("Probe.cs", StringComparison.Ordinal));
        var source = ordinary.Text.Replace("    public static", "#if INCLUDED\n    public static", StringComparison.Ordinal)
            .Replace(";\n}", ";\n#endif\n}", StringComparison.Ordinal)
            .Replace(";\r\n}", ";\r\n#endif\r\n}", StringComparison.Ordinal);
        var first = project with
        {
            ContextId = "first",
            PreprocessorSymbols = ["INCLUDED"],
            // This synthetic variant is not the source currently stored in the real fixture file.
            Documents = [ordinary with { Text = source, DocumentId = "first-doc", IsEditable = false, Fingerprint = "", Options = null }],
        };
        var cases = new List<BundleCase>();
        foreach (var (name, symbols) in new[] { ("linked", new[] { "INCLUDED" }), ("inactive", Array.Empty<string>()) })
        {
            var second = first with { ContextId = "second", PreprocessorSymbols = symbols, Documents = [first.Documents[0] with { DocumentId = "second-doc" }] };
            var snapshot = CompilationSnapshot.Create(first, second) with { RequestId = name };
            var path = _fileSystem.Path.Combine(_fixture.FullName, name + ".json");
            await storage.WriteAsync(path, snapshot);
            var finding = new Finding("DP1004", "Use the empty string literal \"\" instead of string.Empty.", "first-doc",
                source.IndexOf("Text.Empty", StringComparison.Ordinal), "Text.Empty".Length, null);
            var response = new BundleResponse(1, name,
                [new("first", true, [finding]), new("second", true, symbols.Length == 0 ? [] : [finding with { DocumentId = "second-doc" }])], []);
            cases.Add(new BundleCase(name, ["check", path], BundleOutcome.Findings, BundleResponseProtocol.Serialize(response), []));
        }

        _contextCases = cases.ToArray();
    }

    private async Task VerifyAsync()
    {
        foreach (var @case in Cases.Concat(_contextCases))
        {
            foreach (var mode in Enum.GetValues<BundleMode>())
            {
                var result = await ExecuteAsync(mode, @case);
                var prefix = _fileSystem.Path.Combine(OutputDirectory, $"{@case.Name}.{mode}");
                await _fileSystem.File.WriteAllBytesAsync(prefix + ".stdout", result.StandardOutput);
                await _fileSystem.File.WriteAllBytesAsync(prefix + ".stderr", result.StandardError);
            }

            Console.WriteLine($"{@case.Name}: managed/native bytes and exit code {(int)@case.Outcome} match");
        }

        var cli = _fileSystem.Path.Combine(RepositoryRoot, "src/DrillPress.Cli/bin/Release/net10.0/DrillPress.Cli.dll");
        var resultCli = await ProcessRunner.RunAsync("dotnet",
            [cli, "check", "--build-host", BuildHost, "--rules", NativeBundle, _fileSystem.Path.Combine(_fixture.FullName, "Probe.csproj")], RepositoryRoot);
        BundleContract.Validate(Cases.Single(@case => @case.Name == "violating") with { StandardOutput = PublicOutput }, resultCli);
        await _fileSystem.File.WriteAllBytesAsync(_fileSystem.Path.Combine(OutputDirectory, "public.stdout"), PublicOutput);
        Console.WriteLine("CLI/native: complete BuildHost-to-native path matches");
        var coverage = await new RuleCoverageCase(_fileSystem, RepositoryRoot, _fileSystem.Path.Combine(_fixture.FullName, "Coverage"))
            .CreateAsync(BuildHost);
        foreach (var mode in Enum.GetValues<BundleMode>())
        {
            var result = await ExecuteAsync(mode, coverage);
            await _fileSystem.File.WriteAllBytesAsync(_fileSystem.Path.Combine(OutputDirectory, $"all-rules.{mode}.stdout"), result.StandardOutput);
        }

        Console.WriteLine("All five rules and both proposed fixes: managed/native contract matches");
    }
}
