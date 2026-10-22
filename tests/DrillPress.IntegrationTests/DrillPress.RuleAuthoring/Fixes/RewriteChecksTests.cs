using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class RewriteChecksTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Theory]
    [InlineData(
        "namespace Company.Import;\nclass A { void M() { Run(1); } void Run(int value) {} }"
    )]
    [InlineData(
        "namespace Company.Import { class A { void M() { Run(1); } void Run(int value) {} } }"
    )]
    public void Body_edits_keep_bindings_in_qualified_namespaces(string text)
    {
        var workspace = fixture.Workspace();
        var project = workspace.AddProject(
            "Library",
            [new("A.cs", text), new("B.cs", "namespace Company.Other;\nclass B { void M() { } }")]
        );
        var call = Code.Nodes<ExpressionStatementSyntax>()
            .In(workspace.Analyze(TestContext.Current.CancellationToken))
            .Single();
        var proposal = SourceChanges.Propose(
            [SourceChanges.Replace(call.Source, new(call.Syntax.Span.End, 0), " ;")],
            context =>
                RewriteChecks.SameSourceBindings(context.Evidence(call.Source, call.Syntax)!)
                == ProofResult.Proven
        );

        var safe = proposal.IsSafeIn(project);

        Assert.True(safe);
    }

    [Fact]
    public void Declaration_edits_rebind_every_other_file()
    {
        var workspace = fixture.Workspace();
        var project = workspace.AddProject(
            "Library",
            [
                new("A.cs", "class A { public static void Run(object value) {} }"),
                new("B.cs", "class B { void M() { A.Run(\"text\"); } }"),
            ]
        );
        var parameter = Code.Nodes<ParameterSyntax>()
            .In(workspace.Analyze(TestContext.Current.CancellationToken))
            .Single();
        var proposal = SourceChanges.Propose(
            [SourceChanges.Replace(parameter.Source, parameter.Syntax.Type!.Span, "string")],
            context =>
                RewriteChecks.SameSourceBindings(
                    context.Evidence(parameter.Source, parameter.Syntax)!
                ) == ProofResult.Proven
        );

        var safe = proposal.IsSafeIn(project);

        Assert.False(safe);
    }
}
