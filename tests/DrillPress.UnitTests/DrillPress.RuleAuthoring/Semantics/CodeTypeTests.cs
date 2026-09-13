using Xunit;

namespace DrillPress.UnitTests.RuleAuthoring.Semantics;

public sealed class CodeTypeTests
{
    [Theory]
    [InlineData("System.Buffers.ArrayPool<>", "System.Buffers.ArrayPool`1")]
    [InlineData(
        "System.Collections.Generic.Dictionary<,>",
        "System.Collections.Generic.Dictionary`2"
    )]
    [InlineData("Product.Tuple<,,>", "Product.Tuple`3")]
    [InlineData("Product.Tuple< , , >", "Product.Tuple`3")]
    [InlineData("Product.Outer<>+Inner<,>", "Product.Outer`1+Inner`2")]
    [InlineData("Product.Outer<>.Inner<,>", "Product.Outer`1+Inner`2")]
    [InlineData("Product.Outer<>.Inner", "Product.Outer`1+Inner")]
    [InlineData("Product.Outer<>.Inner.Leaf<,>", "Product.Outer`1+Inner+Leaf`2")]
    [InlineData("Product.Outer<,>.Inner<>.Leaf<,,>", "Product.Outer`2+Inner`1+Leaf`3")]
    [InlineData("Product.Outer<>.Inner<,>[,][]", "Product.Outer`1+Inner`2[,][]")]
    [InlineData("Product.Outer<>+Inner.Leaf<>", "Product.Outer`1+Inner+Leaf`1")]
    [InlineData("Product.Outer+Inner<>.Leaf", "Product.Outer+Inner`1+Leaf")]
    [InlineData("Product.Outer<>.Inner`1", "Product.Outer`1+Inner`1")]
    [InlineData("Product.Outer`1+Inner<>", "Product.Outer`1+Inner`1")]
    [InlineData("Product.Outer<>+Inner`1", "Product.Outer`1+Inner`1")]
    [InlineData("Product.Box<>[,][]", "Product.Box`1[,][]")]
    [InlineData("System.Collections.Generic.List`1", "System.Collections.Generic.List`1")]
    [InlineData("System.Console", "System.Console")]
    public void Named_normalizes_open_generic_slots_without_changing_assembly_identity(
        string name,
        string metadataName
    )
    {
        var type = CodeType.Named(name, "Product");

        Assert.Equal(CodeType.Named(metadataName, "Product"), type);
        Assert.Equal(metadataName, type.MetadataName);
        Assert.Equal("", type.TypeArguments);
    }

    [Theory]
    [InlineData("Product.List<")]
    [InlineData("Product.List>")]
    [InlineData("Product.List<int>")]
    [InlineData("Product.List<T>")]
    [InlineData("Product.List<List<>>")]
    [InlineData("Product.Outer<Inner<,>>")]
    [InlineData("Product.List<><>")]
    [InlineData("Product.List<>Tail")]
    [InlineData("Product.List<>>")]
    [InlineData("Product.List`1<>")]
    [InlineData("<>")]
    [InlineData("Product.<>")]
    [InlineData("Product.Outer<>.")]
    [InlineData("Product.Outer<>..Inner")]
    [InlineData("Product.Outer<>.Inner.")]
    [InlineData("Product.Outer<>.Inner..Leaf")]
    [InlineData("Product.Outer<>.Inner<")]
    [InlineData("Product.Outer<>.Inner<int>")]
    [InlineData("Product.Outer<>.Inner<>>")]
    [InlineData("Product.Outer<>.Inner`1<>")]
    [InlineData("Product.Outer<>.Inner<>Tail")]
    [InlineData("Product.Outer<>.Inner[].Leaf")]
    [InlineData("Product.Outer<>.+Inner")]
    [InlineData("Product.Outer<>.<>")]
    public void Named_rejects_invalid_or_constructed_generic_syntax(string name)
    {
        Assert.Throws<ArgumentException>("metadataName", () => CodeType.Named(name));
    }

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
            "DrillPress.UnitTests.RuleAuthoring.Semantics.CodeTypeTests+Nesting+Contained",
            type.MetadataName
        );
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
