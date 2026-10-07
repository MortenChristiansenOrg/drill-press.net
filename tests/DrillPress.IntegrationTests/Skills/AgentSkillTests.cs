using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.Skills;

public sealed class AgentSkillTests(AgentSkillFixture fixture) : IClassFixture<AgentSkillFixture>
{
    public static TheoryData<string, string> Examples => AgentSkillFixture.ReadExamples();

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task Skill_examples_compile_against_the_public_SDK_and_their_tests_pass(
        string name,
        string code
    )
    {
        var problems = await fixture.CheckAsync(name, code);

        Assert.Empty(problems);
    }

    [Fact]
    public void Skill_references_resolve_to_bundled_files()
    {
        var broken = fixture.BrokenLinks();

        Assert.Empty(broken);
    }

    [Fact]
    public void Skill_metadata_names_its_directory_and_fits_agent_limits()
    {
        var (name, description) = fixture.Metadata();

        Assert.Equal("drillpress-rules", name);
        Assert.InRange(description.Length, 1, 1024);
    }
}
