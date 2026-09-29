using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Queries;

public sealed class TestDiscoveryTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void V3_interface_markers_require_discoverers_and_default_interface_bodies_supply_tests()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Tests",
            [
                new(
                    "Tests.cs",
                    """
                    using System;
                    [Xunit.v3.XunitTestCaseDiscoverer(typeof(Xunit.v3.FactDiscoverer))]
                    class CustomAttribute : PlainAttribute {}
                    class PlainAttribute : Attribute, Xunit.v3.IFactAttribute {
                        public bool DisableParallelization => false;
                        public string? DisplayName => null;
                        public bool Explicit => false;
                        public string? Skip => null;
                        public Type[]? SkipExceptions => null;
                        public Type? SkipType => null;
                        public string? SkipUnless => null;
                        public string? SkipWhen => null;
                        public string? SourceFilePath => null;
                        public int? SourceLineNumber => null;
                        public int Timeout => 0;
                    }
                    interface Defaults { [Xunit.Fact] void Test() {} }
                    interface Abstract { [Xunit.Fact] void NotImplemented(); }
                    class DefaultTests : Defaults {}
                    class AbstractImplementation : Abstract { public void NotImplemented() {} }
                    class CustomTests { [Custom] public void Custom() {} }
                    class NoDiscoverer { [Plain] public void Unregistered() {} }
                    """
                ),
            ],
            isTest: true
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var classes = Code.TestClasses.In(solution).Select(type => type.Name).ToArray();

        Assert.Equal(["DefaultTests", "CustomTests"], classes);
    }

    [Theory]
    [InlineData("xunit.core")]
    [InlineData("xunit.v3.core.aot")]
    public void Additional_framework_marker_assemblies_are_recognized(string assembly)
    {
        var workspace = fixture.Workspace();
        var compilation = workspace
            .AddProject("References", [new("Ref.cs", "class Ref {}")])
            .Compilation;
        var references = compilation
            .References.Where(reference =>
                compilation.GetAssemblyOrModuleSymbol(reference)
                    is not Microsoft.CodeAnalysis.IAssemblySymbol assemblySymbol
                || !assemblySymbol.Name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();
        var framework = workspace.AddProject(
            assembly,
            [new("Fact.cs", "namespace Xunit; public class FactAttribute : System.Attribute {}")],
            references: references
        );
        workspace.AddProject(
            "Tests",
            [new("Tests.cs", "class C { [Xunit.Fact] public void Test() {} }")],
            isTest: true,
            dependencies: [framework],
            references: references
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var names = Code.TestMethods.In(solution).Select(method => method.Name).ToArray();
        var classes = Code.TestClasses.In(solution).Select(type => type.Name).ToArray();

        Assert.Equal(["Test"], names);
        Assert.Equal(["C"], classes);
    }

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
