using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring;

public sealed class CodeTypeTests
{
    [Fact]
    public void Of_uses_the_generic_type_definition_metadata_name()
    {
        var type = CodeType.Of<Dictionary<string, int>>();

        Assert.Equal("System.Collections.Generic.Dictionary`2", type.MetadataName);
    }

    [Fact]
    public void Of_uses_nested_type_metadata_names()
    {
        var type = CodeType.Of<Nesting.Contained>();

        Assert.Equal(
            "DrillPress.UnitTests.RuleAuthoring.CodeTypeTests+Nesting+Contained",
            type.MetadataName);
    }

    [Fact]
    public void Named_rejects_a_blank_metadata_name()
    {
        Assert.Throws<ArgumentException>(() => CodeType.Named(" "));
    }

    private static class Nesting
    {
        public sealed class Contained;
    }
}
