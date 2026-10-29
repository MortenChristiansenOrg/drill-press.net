using System.IO.Abstractions.TestingHelpers;
using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DrillPress.UnitTests.Engine;

public sealed class AnalysisEngineReuseTests
{
    [Fact]
    public async Task A_changed_collection_scope_does_not_retain_previous_coverage_evidence()
    {
        var fixture = new CoverageFixture();
        var engine = new AnalysisEngine(
            fixture.FileSystem,
            fixture.Process,
            reuseCompilations: true
        );
        var collect = true;
        var rules = new RuleCatalog();
        rules
            .Rule("COV", "Exercise call.")
            .For(Code.Calls.ToMethodsNamed("Hit"))
            .CollectCoverageIn(Code.Projects.Where(_ => collect))
            .Require(global::DrillPress.Coverage.Executed);

        var collected = await engine.AnalyzeAsync(
            rules,
            fixture.Snapshot,
            TestContext.Current.CancellationToken
        );
        collect = false;
        var unprepared = await engine.AnalyzeAsync(
            rules,
            fixture.Snapshot,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(collected);
        Assert.Equal(
            ["coverage: unknown (not-prepared)"],
            unprepared.Select(diagnostic => diagnostic.Evidence)
        );
        Assert.Equal(1, fixture.Process.Collections);
    }

    [Fact]
    public void Source_edits_reconstruct_only_the_changed_context_and_its_dependents()
    {
        var engine = new AnalysisEngine(
            new MockFileSystem(),
            new CoverageProcess(),
            reuseCompilations: true
        );
        var dependency = TestSnapshots.CreateProject("A.cs", "public class A {}") with
        {
            Name = "A",
            AssemblyName = "A",
        };
        var consumer = TestSnapshots.CreateProject("B.cs", "public class B {}") with
        {
            Name = "B",
            AssemblyName = "B",
            ReferencedContextIds = [dependency.ContextId],
            CompilationReferences = [new(dependency.ContextId, [], false)],
        };
        var unrelated = TestSnapshots.CreateProject("C.cs", "public class C {}") with
        {
            Name = "C",
            AssemblyName = "C",
        };
        var snapshot = CompilationSnapshot.Create(consumer, unrelated, dependency);
        var changed = snapshot with
        {
            Projects =
            [
                consumer,
                unrelated,
                dependency with
                {
                    Documents = [dependency.Documents[0] with { Text = "public class Renamed {}" }],
                },
            ],
        };

        var original = engine.Reconstruct(snapshot, TestContext.Current.CancellationToken);
        var updated = engine.Reconstruct(changed, TestContext.Current.CancellationToken);

        Assert.Equal(
            [("B", false), ("C", true), ("A", false)],
            original.Zip(
                updated,
                (before, after) =>
                    (after.Snapshot.Name, ReferenceEquals(before.Compilation, after.Compilation))
            )
        );
        Assert.Same(
            updated[2].Compilation,
            Assert
                .Single(
                    updated[0]
                        .Compilation.References.OfType<Microsoft.CodeAnalysis.CompilationReference>()
                )
                .Compilation
        );
        Assert.NotNull(updated[0].Compilation.GetTypeByMetadataName("Renamed"));
    }

    [Fact]
    public async Task Repeated_checks_evaluate_rules_again_and_use_each_request_identity()
    {
        var engine = new AnalysisEngine(
            new MockFileSystem(),
            new CoverageProcess(),
            reuseCompilations: true
        );
        var snapshot = TestSnapshots.Create("class A {}", "A.cs");
        var evaluations = 0;
        var rules = new RuleCatalog();
        rules
            .Rule("TEST001", "Rename this class.")
            .For(Code.Nodes<ClassDeclarationSyntax>())
            .Forbid(fix: node =>
            {
                evaluations++;
                return null;
            });
        var next = snapshot with { RequestId = "next-request" };

        var first = await engine.EvaluateAsync(
            rules,
            snapshot,
            TestContext.Current.CancellationToken
        );
        var second = await engine.EvaluateAsync(rules, next, TestContext.Current.CancellationToken);

        Assert.Equal(2, evaluations);
        Assert.Equal(
            BundleResponseProtocol.Serialize(first with { RequestId = "next-request" }),
            BundleResponseProtocol.Serialize(second)
        );
    }

    [Fact]
    public async Task Changed_source_replaces_bound_candidates()
    {
        var engine = new AnalysisEngine(
            new MockFileSystem(),
            new CoverageProcess(),
            reuseCompilations: true
        );
        var rules = new RuleCatalog();
        rules
            .Rule("TEST001", "Rename class A.")
            .For(
                Code.Nodes<ClassDeclarationSyntax>()
                    .Where(node => node.Syntax.Identifier.ValueText == "A")
            )
            .Forbid();
        var first = TestSnapshots.Create("class A {}", "A.cs");
        var changed = first with
        {
            RequestId = "changed-request",
            Projects =
            [
                first.Projects[0] with
                {
                    Documents = [first.Projects[0].Documents[0] with { Text = "class B {}" }],
                },
            ],
        };

        var original = await engine.EvaluateAsync(
            rules,
            first,
            TestContext.Current.CancellationToken
        );
        var updated = await engine.EvaluateAsync(
            rules,
            changed,
            TestContext.Current.CancellationToken
        );

        Assert.Single(original.Contexts[0].Findings);
        Assert.Empty(updated.Contexts[0].Findings);
        Assert.Equal("changed-request", updated.RequestId);
    }
}
