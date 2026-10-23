using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Fixes;

public sealed class FixProposalTests
{
    [Theory]
    [InlineData("no\tclock in scope")]
    [InlineData("no clock\nin scope")]
    [InlineData("no clock\u0085in scope")]
    [InlineData("no clock\u2028in scope")]
    [InlineData("no clock\u2029in scope")]
    public void A_withheld_reason_with_a_control_character_or_line_separator_is_rejected(
        string reason
    )
    {
        var exception = Assert.Throws<ArgumentException>(() => FixProposal.Withheld(reason));

        Assert.Equal(
            "The reason must be a single line without control characters. (Parameter 'reason')",
            exception.Message
        );
    }

    [Fact]
    public void A_withheld_reason_keeps_its_single_line_text()
    {
        var proposal = FixProposal.Withheld("no TimeProvider is in scope");

        Assert.Equal("no TimeProvider is in scope", proposal.WithheldReason);
    }
}
