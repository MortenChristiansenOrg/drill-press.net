using System.IO.Abstractions;
using DrillPress.Manifest;

namespace DrillPress.Engine;

/// <summary>Hosts a compiled rule set behind the executable rule-bundle contract.</summary>
public sealed class RuleApplication
{
    private readonly IFileSystem _fileSystem;

    /// <summary>Creates a rule-bundle host for local compilation snapshots.</summary>
    public RuleApplication() : this(new FileSystem())
    {
    }

    internal RuleApplication(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Executes the rule-bundle command, writes the internal response, and returns the
    /// clean, findings, or failure exit code understood by the coordinator.
    /// </summary>
    public async Task<RuleExitCode> RunAsync(
        RuleSet rules,
        string[] args,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default)
    {
        standardOutput ??= Console.Out;
        standardError ??= Console.Error;
        if (args is not ["check", var snapshotPath])
        {
            await standardError.WriteLineAsync("Usage: <rule-bundle> check <snapshot>");
            return RuleExitCode.Failure;
        }

        try
        {
            var snapshot = await new CompilationSnapshotFile(_fileSystem).ReadAsync(snapshotPath, cancellationToken);
            var response = await new AnalysisEngine(_fileSystem).EvaluateAsync(rules, snapshot, cancellationToken);
            _ = new BundleResponseValidator().Validate(snapshot, response);
            await standardOutput.WriteAsync(System.Text.Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response)));
            return response.Contexts.All(context => context.Findings.Length == 0) ? RuleExitCode.Clean : RuleExitCode.Findings;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await standardError.WriteLineAsync($"drillpress-rules: {exception.Message}");
            return RuleExitCode.Failure;
        }
    }

}
