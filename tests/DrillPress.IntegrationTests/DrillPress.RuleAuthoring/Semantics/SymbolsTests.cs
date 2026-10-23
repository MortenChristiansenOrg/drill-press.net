using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Semantics;

public sealed class SymbolsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Overrides_interface_implementations_and_event_handlers_are_told_apart()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    using System;
                    interface I { void Contract(); void Explicit(); }
                    class Base { public virtual void Override() { } }
                    class Changed : EventArgs { }
                    class A : Base, I {
                        public void Contract() { }
                        void I.Explicit() { }
                        public override void Override() { }
                        public void Clicked(object sender, EventArgs args) { }
                        public void Changes(object sender, Changed args) { }
                        public void Own(string sender, EventArgs args) { }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var methods = Code
            .Methods.In(solution)
            .Where(method => method.Symbol!.ContainingType.Name == "A")
            .Select(method =>
                $"{method.Name}:{method.Symbol!.ImplementsOrOverrides()}:{method.Symbol!.IsEventHandler()}"
            )
            .ToArray();

        Assert.Equal(
            [
                "Contract:True:False",
                "Explicit:True:False",
                "Override:True:False",
                "Clicked:False:True",
                "Changes:False:True",
                "Own:False:False",
            ],
            methods
        );
    }
}
