using Sample.WidgetLibrary;
using Xunit;

namespace Sample.WidgetLibrary.Tests;

public sealed class WidgetTests
{
    // csharpier-ignore - Blank-line and assertion positions exercise DP1001 and DP1002.
    [Fact]
    public void Store_returns_empty_value()
    {
        var store = new FileWidgetStore();

        Assert.Equal(string.Empty, store.Read());

        var result = store.Read();

        Assert.True(result.Length == 0);
    }

    // csharpier-ignore - This is the valid two-blank-line case for the test-layout rules.
    [Theory]
    public void Formatting_is_stable()
    {
        var formatter = new PlainWidgetFormatter();

        var result = formatter.Format("hello");

        Assert.Equal("hello", result);
    }

    // csharpier-ignore - Assert.Throws before the final blank line is an allowed act statement.
    [Fact]
    public void Invalid_input_throws()
    {
        Action action = () => throw new InvalidOperationException("invalid");

        var exception = Assert.Throws<InvalidOperationException>(action);

        _ = exception.Message;
    }
}

// Test implementations are deliberately excluded by DP1003.
public sealed class InMemoryWidgetStore : IWidgetStore
{
    public string Read() => "test";
}
