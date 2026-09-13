using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Semantics;

public sealed class CodeMemberTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Type_owned_members_reject_blank_names(string name)
    {
        var type = CodeType.Named("System.Console");

        Assert.Throws<ArgumentException>(() => type.Member(name));
    }
}
