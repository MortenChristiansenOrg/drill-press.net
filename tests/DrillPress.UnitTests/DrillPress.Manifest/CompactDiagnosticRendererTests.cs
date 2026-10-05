using System.Globalization;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class CompactDiagnosticRendererTests
{
    [Fact]
    public void Detailed_coverage_rendering_explains_mapping_limits_and_matching_ranges()
    {
        var files = new MockFileSystem();
        var finding = new AggregatedFinding(
            "COV",
            "Exercise call.",
            "file",
            "Target.cs",
            0,
            3,
            1,
            1,
            null
        )
        {
            Evidence = "coverage: unknown (unsupported-mapping)",
            Coverage =
            [
                new(
                    ExecutionCoverage.Unknown,
                    [CoverageReason.UnsupportedExpressionMapping],
                    "Target",
                    "net10.0",
                    "context"
                )
                {
                    Ranges = [new("evidence", "Evidence.cs", 0, 10, ExecutionCoverage.Covered)],
                },
            ],
        };
        var result = new ValidatedResult([finding], [], []);

        var output = new CompactDiagnosticRenderer(files).Render(result, explainCoverage: true);

        Assert.Equal(
            """
            COV Exercise call.
            Target.cs
              1 [coverage: unknown (unsupported-mapping)]
                Execution Target net10.0 context=context
                UnsupportedExpressionMapping: The current mapping cannot distinguish this expression; additional tests alone may not resolve it.
                range Evidence.cs 0+10: covered

            """.ReplaceLineEndings("\n"),
            output
        );
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("da-DK")]
    [InlineData("tr-TR")]
    [InlineData("ar-EG")]
    public async Task Output_is_ordinal_escaped_LF_and_culture_independent(string culture)
    {
        var fileSystem = new MockFileSystem();
        var directory = fileSystem.Path.GetFullPath("render-root");
        fileSystem.Directory.CreateDirectory(directory);
        fileSystem.Directory.SetCurrentDirectory(directory);
        var result = new ValidatedResult(
            [
                new("Z", "Replace Z.", "z", fileSystem.Path.GetFullPath("z.cs"), 20, 1, 3, 1, null),
                new(
                    "A",
                    "Replace A.",
                    "a",
                    fileSystem.Path.GetFullPath(" a\"\t.cs "),
                    3,
                    1,
                    1,
                    4,
                    "fix"
                ),
                new(
                    "A",
                    "Replace A.",
                    "a",
                    fileSystem.Path.GetFullPath(" a\"\t.cs "),
                    0,
                    1,
                    1,
                    1,
                    null
                ),
            ],
            [],
            []
        );
        var renderer = new CompactDiagnosticRenderer(fileSystem);

        var output = await Task.Run(
            () =>
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                return Encoding.UTF8.GetBytes(renderer.Render(result));
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            Encoding.UTF8.GetBytes(
                """
                A Replace A.
                " a\u0022\t.cs "
                  1
                  +1:4
                Z Replace Z.
                z.cs
                  3

                """.ReplaceLineEndings("\n")
            ),
            output
        );
    }

    [Fact]
    public void Linked_multitarget_fixable_golden_uses_UTF16_coordinates()
    {
        var fixture = new ContractFixture();
        var result = new BundleResponseValidator().Validate(fixture.Snapshot, fixture.Response);

        var output = new CompactDiagnosticRenderer(new MockFileSystem()).Render(result);

        Assert.Equal("DP1004 Replace alpha.\nShared.cs\n  +1:3\n", output);
    }

    [Fact]
    public void Clean_public_golden_is_empty()
    {
        var result = new ValidatedResult([], [], []);

        var output = new CompactDiagnosticRenderer(new MockFileSystem()).Render(result);

        Assert.Equal("", output);
    }

    [Fact]
    public void Complexity_is_optional_and_appears_once_per_classified_rule_group()
    {
        var fileSystem = new MockFileSystem();
        var first = new AggregatedFinding(
            "A",
            "Fix A.",
            "file",
            fileSystem.Path.GetFullPath("File.cs"),
            0,
            1,
            1,
            1,
            null
        )
        {
            FixComplexity = RuleFixComplexity.Local,
        };
        var result = new ValidatedResult(
            [
                first,
                first with
                {
                    Start = 2,
                    Line = 2,
                },
                first with
                {
                    RuleId = "B",
                    Message = "Fix B.",
                    FixComplexity = null,
                },
            ],
            [],
            []
        );
        var renderer = new CompactDiagnosticRenderer(fileSystem);

        var standard = renderer.Render(result);
        var annotated = renderer.Render(result, showFixComplexity: true);

        Assert.Equal(
            """
            A Fix A.
            File.cs
              1
              2
            B Fix B.
            File.cs
              1

            """.ReplaceLineEndings("\n"),
            standard
        );
        Assert.Equal(
            """
            A [fix:local] Fix A.
            File.cs
              1
              2
            B Fix B.
            File.cs
              1

            """.ReplaceLineEndings("\n"),
            annotated
        );
    }
}
