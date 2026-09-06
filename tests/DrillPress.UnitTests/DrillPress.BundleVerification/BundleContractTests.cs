using System.Text;
using DrillPress.BundleVerification;
using Xunit;

namespace DrillPress.UnitTests.DrillPress.BundleVerification;

public sealed class BundleContractTests
{
    [Theory]
    [InlineData(1, "out", "err")]
    [InlineData(0, "changed", "err")]
    [InlineData(0, "out", "changed")]
    public void Rejects_different_exit_codes_or_output(int exitCode, string stdout, string stderr)
    {
        var expected = new BundleCase("probe", [], BundleOutcome.Clean, "out"u8.ToArray(), "err"u8.ToArray());
        var actual = new ProcessOutput(exitCode, Encoding.UTF8.GetBytes(stdout), Encoding.UTF8.GetBytes(stderr));

        var exception = Assert.Throws<InvalidOperationException>(() => BundleContract.Validate(expected, actual));

        Assert.Equal("probe: bundle output differs from the exact byte/exit contract.", exception.Message);
    }

    [Fact]
    public void Accepts_identical_bytes_including_non_text_values()
    {
        var expected = new BundleCase("probe", [], BundleOutcome.Findings, [0, 255], [13, 10]);
        var actual = new ProcessOutput(1, [0, 255], [13, 10]);

        BundleContract.Validate(expected, actual);
    }
}
