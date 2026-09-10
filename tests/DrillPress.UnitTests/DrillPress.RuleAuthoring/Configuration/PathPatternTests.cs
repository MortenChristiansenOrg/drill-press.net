using DrillPress.Configuration;
using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Configuration;

public sealed class PathPatternTests
{
    [Theory]
    [InlineData("**/Tracing/*.cs", "Tracing/Log.cs", true)]
    [InlineData("**/Tracing/*.cs", "src\\Tracing\\Log.cs", true)]
    [InlineData("**/Tracing/*.cs", "Tracing/Nested/Log.cs", false)]
    [InlineData("**/Tracing/*.cs", "tracing/Log.cs", false)]
    [InlineData("a?.cs", "ab.cs", true)]
    [InlineData("a?.cs", "abc.cs", false)]
    [InlineData("__RECURSIVE__/*.cs", "elsewhere/File.cs", false)]
    public void Patterns_match_exact_normalized_paths(string pattern, string path, bool expected)
    {
        var matcher = new PathPattern(pattern);

        var actual = matcher.Matches(path);

        Assert.Equal(expected, actual);
    }
}
