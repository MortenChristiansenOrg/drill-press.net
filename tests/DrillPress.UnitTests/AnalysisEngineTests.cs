using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.SampleRules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DrillPress.UnitTests;

public sealed class AnalysisEngineTests : IDisposable
{
    private readonly List<string> _snapshotPaths = [];

    [Fact]
    public async Task Finds_only_the_string_empty_reference_at_its_physical_location()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string source = """
            namespace Sample;

            public static class Values
            {
                public static string Violation => string.Empty;
                public static string Compliant => "";
            }
            """;
        var sourcePath = Path.Combine(Path.GetTempPath(), "drillpress-tests", "Values.cs");
        var snapshotPath = await WriteSnapshotAsync(sourcePath, source, cancellationToken);

        var diagnostics = await AnalysisEngine.AnalyzeAsync(
            SampleRuleSet.Create(), snapshotPath, cancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DP1004", diagnostic.Descriptor.Id);
        Assert.Equal(sourcePath, diagnostic.Location.FilePath);
        Assert.Equal(5, diagnostic.Location.Line);
        Assert.Equal(39, diagnostic.Location.Column);
        Assert.Equal("string.Empty", source.Substring(diagnostic.Location.Start, diagnostic.Location.Length));
    }

    [Fact]
    public async Task Rule_application_returns_clean_for_a_compliant_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotPath = await WriteSnapshotAsync(
            Path.Combine(Path.GetTempPath(), "drillpress-tests", "Clean.cs"),
            "public static class Clean { public static string Value => \"\"; }",
            cancellationToken);
        var output = new StringWriter();

        var exitCode = await RuleApplication.RunAsync(
            SampleRuleSet.Create(), ["check", snapshotPath], output, TextWriter.Null, cancellationToken);

        Assert.Equal(RuleExitCode.Clean, exitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task Rule_application_returns_findings_for_a_violating_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotPath = await WriteSnapshotAsync(
            Path.Combine(Path.GetTempPath(), "drillpress-tests", "Violation.cs"),
            "public static class Violation { public static string Value => string.Empty; }",
            cancellationToken);
        var output = new StringWriter();

        var exitCode = await RuleApplication.RunAsync(
            SampleRuleSet.Create(), ["check", snapshotPath], output, TextWriter.Null, cancellationToken);

        Assert.Equal(RuleExitCode.Findings, exitCode);
        Assert.Contains("DP1004", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rule_application_returns_failure_for_invalid_arguments()
    {
        var error = new StringWriter();

        var exitCode = await RuleApplication.RunAsync(
            SampleRuleSet.Create(), ["check"], TextWriter.Null, error, TestContext.Current.CancellationToken);

        Assert.Equal(RuleExitCode.Failure, exitCode);
        Assert.Contains("Usage:", error.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var snapshotPath in _snapshotPaths)
        {
            File.Delete(snapshotPath);
        }
    }

    private async Task<string> WriteSnapshotAsync(
        string sourcePath,
        string source,
        CancellationToken cancellationToken)
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("The runtime did not expose its trusted platform assemblies.");
        var references = trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path =>
                Path.GetFileName(path) is "System.Private.CoreLib.dll" or "System.Runtime.dll" or "netstandard.dll")
            .ToArray();
        var snapshot = CompilationSnapshot.Create(new ProjectSnapshot(
            "Sample",
            "Sample",
            Path.ChangeExtension(sourcePath, ".csproj"),
            (int)LanguageVersion.CSharp14,
            (int)OutputKind.DynamicallyLinkedLibrary,
            (int)NullableContextOptions.Enable,
            [],
            [new DocumentSnapshot(sourcePath, source, IsGenerated: false)],
            references));
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.drillpress.json");
        await snapshot.WriteAsync(snapshotPath, cancellationToken);
        _snapshotPaths.Add(snapshotPath);
        return snapshotPath;
    }
}
