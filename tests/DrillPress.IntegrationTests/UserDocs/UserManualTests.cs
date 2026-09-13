using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.UserDocs;

public sealed class UserManualTests(UserManualFixture fixture) : IClassFixture<UserManualFixture>
{
    public static TheoryData<string, string, string> Examples => UserManualFixture.ReadExamples();

    [Theory]
    [MemberData(nameof(Examples))]
    public void Complete_examples_compile_against_the_public_SDK(
        string name,
        string kind,
        string code
    )
    {
        var errors = fixture.Compile(name, kind, code);

        Assert.Empty(errors);
    }

    [Fact]
    public void Local_navigation_and_assets_resolve()
    {
        var broken = fixture.BrokenLinks();

        Assert.Empty(broken);
    }

    [Fact]
    public void Manual_contains_checked_examples()
    {
        var examples = Examples;

        Assert.NotEmpty(examples);
    }
}
