using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class TestDiscoveryTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Test_methods_are_written_once_and_override_inheritance_respects_attribute_usage()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Tests",
            [
                new(
                    "Tests.cs",
                    """
                    class CustomAttribute : Xunit.FactAttribute {}
                    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false)]
                    class NonInheritedAttribute : Xunit.FactAttribute {}
                    abstract class Base {
                        [Custom] public virtual void Written() {}
                        [Xunit.Theory] public void Data(int value) {}
                    }
                    class First : Base {}
                    class Second : Base { public override void Written() {} }
                    abstract class HiddenBase { [NonInherited] public virtual void Hidden() {} }
                    class HiddenChild : HiddenBase { public override void Hidden() {} }
                    class Empty {}
                    """
                ),
            ],
            isTest: true
        );
        workspace.AddProject(
            "Lookalikes",
            [
                new(
                    "Fake.cs",
                    "namespace Xunit { class FactAttribute : System.Attribute {} } class Fake { [Xunit.Fact] void Test() {} }"
                ),
            ],
            isTest: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var methods = Code
            .TestMethods.In(solution)
            .Select(method => $"{method.ContainingType?.Name}.{method.Name}")
            .ToArray();
        var classes = Code.TestClasses.In(solution).Select(type => type.Name).ToArray();
        var includingBases = TestDiscovery
            .Classes(includeAbstract: true)
            .In(solution)
            .Select(type => type.Name)
            .ToArray();

        Assert.Equal(["Base.Written", "Base.Data", "Second.Written", "HiddenBase.Hidden"], methods);
        Assert.Equal(["First", "Second"], classes);
        Assert.Equal(["Base", "First", "Second", "HiddenBase"], includingBases);
    }
}
