using DrillPress.Collections;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Collections;

public sealed class SetComparisonTests
{
    [Fact]
    public void Differences_ignore_duplicates_and_use_configured_identity()
    {
        var expected = new[] { "json", "JSON", "xml" };
        var actual = new[] { "Json", "csv" };

        var comparison = new SetComparison<string>(expected, actual, StringComparer.OrdinalIgnoreCase);

        Assert.Equal<string>(["xml"], comparison.Missing);
        Assert.Equal<string>(["csv"], comparison.Unexpected);
        Assert.False(comparison.AreEqual);
    }
}
