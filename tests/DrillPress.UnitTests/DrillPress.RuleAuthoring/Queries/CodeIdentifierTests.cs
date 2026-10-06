using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Queries;

public sealed class CodeIdentifierTests
{
    [Theory]
    [InlineData("Get_ReturnsNullForMissing", new[] { "Get", "Returns", "Null", "For", "Missing" })]
    [InlineData("HTTPClientFactory2", new[] { "HTTP", "Client", "Factory", "2" })]
    [InlineData("@__httpClient42URL__", new[] { "http", "Client", "42", "URL" })]
    [InlineData("ÆbleØnske", new[] { "Æble", "Ønske" })]
    [InlineData("𐐀𐐨Client", new[] { "𐐀𐐨", "Client" })]
    [InlineData("ABC", new[] { "ABC" })]
    [InlineData("___", new string[0])]
    public void Splitting_preserves_words_acronyms_and_digit_groups(
        string identifier,
        string[] expected
    )
    {
        var words = CodeIdentifier.Words(identifier);

        Assert.Equal(expected, words);
    }

    [Theory]
    [InlineData("ReturnsNull", "null", StringComparison.OrdinalIgnoreCase, true)]
    [InlineData("ReturnsNull", "null", StringComparison.Ordinal, false)]
    [InlineData("ReturnsNullable", "null", StringComparison.OrdinalIgnoreCase, false)]
    public void Matching_requires_a_complete_word(
        string name,
        string word,
        StringComparison comparison,
        bool expected
    )
    {
        var result = CodeIdentifier.ContainsWord(name, word, comparison);

        Assert.Equal(expected, result);
    }
}
