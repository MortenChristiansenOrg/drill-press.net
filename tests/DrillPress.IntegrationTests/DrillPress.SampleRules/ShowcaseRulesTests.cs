using DrillPress.Baselines;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using DrillPress.SampleRules;
using Xunit;

namespace DrillPress.IntegrationTests.SampleRules;

public sealed class ShowcaseRulesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public async Task Composed_policies_accept_guarded_text_matching_formats_and_referenced_examples()
    {
        var workspace = fixture.Workspace();
        var production = workspace.AddProject(
            "CodecExamples",
            [
                new(
                    "Existing.Legacy.cs",
                    """
                    namespace CodecExamples;
                    public interface ITextCodec { }
                    public class JsonCodec : ITextCodec
                    {
                        const string Example = "This is one deliberately long protocol example, kept only once so it needs no shared declaration.";
                        public string Normalize(string? text)
                        {
                            if (text is null) return "";
                            return text.Trim();
                        }
                        public async System.Threading.Tasks.Task Read() => await System.Threading.Tasks.Task.CompletedTask;
                        public int Decode() { var scratchBuffer = 1; return scratchBuffer; }
                        internal class State { }
                    }
                    class ReaderFormats { const string Json = "json"; }
                    class WriterFormats { const string Json = "json"; }
                    """
                ),
                new(
                    "Tracing/ConsoleTrace.cs",
                    "public class ConsoleTrace { void Write() => System.Console.WriteLine(\"trace\"); }"
                ),
            ]
        );
        workspace.AddProject(
            "CodecExamples.Tests",
            [new("Tests.cs", "class Tests { void JsonCodecRoundTrip() { } }")],
            isTest: true,
            dependencies: [production]
        );
        workspace.AddProject(
            "Unrelated",
            [
                new(
                    "Outside.cs",
                    "internal class Outside { void M() => System.Console.WriteLine(System.Guid.NewGuid()); }"
                ),
            ]
        );
        var accepted = new SourceBaseline(workspace.Analyze(TestContext.Current.CancellationToken));

        var result = await workspace.CheckAsync(
            ShowcaseRules.Create(accepted),
            TestContext.Current.CancellationToken
        );

        Assert.Empty(result.Findings);
        Assert.Equal(production.Sources[0].Document.Text, result.FixedText("Existing.Legacy.cs"));
    }

    [Fact]
    public async Task Console_policy_keeps_the_exact_call_anchor_outside_the_tracing_adapter()
    {
        var workspace = fixture.Workspace();
        const string source =
            "public class Example { void Trace() => System.Console.WriteLine(\"trace\"); }";
        workspace.AddProject("CodecExamples", [new("ConsoleTrace.cs", source)]);

        var result = await workspace.CheckAsync(
            ShowcaseRules.Create(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            [
                new TestFinding(
                    "SDK2002",
                    "ConsoleTrace.cs",
                    1,
                    40,
                    "System.Console.WriteLine(\"trace\")",
                    false
                ),
            ],
            result.Findings
        );
        Assert.Equal(source, result.FixedText("ConsoleTrace.cs"));
    }

    [Fact]
    public async Task Shared_facts_flow_inventories_packages_and_baselines_produce_independent_findings()
    {
        var baseline = new SourceBaseline(new AnalysisSolution([]));
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "CodecExamples",
            [
                new(
                    "Samples.Legacy.cs",
                    """
                    namespace CodecExamples;
                    [System.Serializable] public class Samples
                    {
                        const string First = "This is a deliberately long protocol example that should have one shared declaration across the codec tests.";
                        const string Second = "This is a deliberately long protocol example that should have one shared declaration across the codec tests.";
                        string Normalize(string? text) => text.Trim();
                        System.Func<int> Capture() { var scratchBuffer = 1; return () => scratchBuffer; }
                        int Read(int value) => value switch { 1 => 100, 2 => 200, 3 => 300, 4 => 400, _ => 0 };
                        int Write(int value) => value switch { 1 => 100, 2 => 200, 3 => 300, 4 => 400, _ => 0 };
                    }
                    class ReaderFormats { const string Json = "json"; }
                    class WriterFormats { const string Xml = "xml"; }
                    """
                ),
            ],
            packages: [new PackageReferenceSnapshot("Newtonsoft.Json", "13.0.3")]
        );

        var result = await workspace.CheckAsync(
            ShowcaseRules.Create(baseline),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            [
                "SDK2005",
                "SDK2005",
                "SDK2006",
                "SDK2006",
                "SDK2007",
                "SDK2008",
                "SDK2009",
                "SDK2010",
                "SDK2012",
                "SDK2013",
            ],
            result.Findings.Select(finding => finding.Rule)
        );
        Assert.All(result.Findings, finding => Assert.False(finding.HasFix));
    }

    [Fact]
    public async Task Codec_policies_find_semantic_and_architectural_violations()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "CodecExamples",
            [
                new(
                    "Codec.cs",
                    """
                    namespace CodecExamples;
                    public interface ITextCodec { }
                    internal class JsonCodec : ITextCodec
                    {
                        async System.Threading.Tasks.Task Read()
                        {
                            Pause();
                            await System.Threading.Tasks.Task.CompletedTask;
                        }
                        void Pause() => System.Threading.Thread.Sleep(1);
                        void Trace() => System.Console.WriteLine(System.Guid.NewGuid());
                    }
                    """
                ),
            ]
        );

        var result = await workspace.CheckAsync(
            ShowcaseRules.Create(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            ["SDK2001", "SDK2002", "SDK2003", "SDK2004", "SDK2011"],
            result.Findings.Select(finding => finding.Rule)
        );
        Assert.Equal(
            [false, false, false, false, true],
            result.Findings.Select(finding => finding.HasFix)
        );
    }

    [Fact]
    public async Task Codec_counterparts_follow_project_dependencies()
    {
        var workspace = fixture.Workspace();
        var production = workspace.AddProject(
            "CodecExamples",
            [
                new(
                    "Codec.cs",
                    "namespace CodecExamples; public interface ITextCodec { } public class JsonCodec : ITextCodec { }"
                ),
            ]
        );
        workspace.AddProject(
            "CodecExamples.Tests",
            [new("Tests.cs", "class Tests { void JsonCodecRoundTrip() { } }")],
            isTest: true,
            dependencies: [production]
        );

        var result = await workspace.CheckAsync(
            ShowcaseRules.Create(),
            TestContext.Current.CancellationToken
        );

        Assert.Empty(result.Findings);
    }
}
