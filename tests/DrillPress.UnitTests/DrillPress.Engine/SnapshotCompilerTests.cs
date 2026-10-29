using System.IO.Abstractions.TestingHelpers;
using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Engine;

public sealed class SnapshotCompilerTests
{
    [Fact]
    public void Source_edits_reconstruct_only_the_changed_context_and_its_dependents()
    {
        var compiler = new SnapshotCompiler(new MockFileSystem());
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

        var original = compiler.Reconstruct(snapshot, TestContext.Current.CancellationToken);
        var updated = compiler.Reconstruct(changed, TestContext.Current.CancellationToken);

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
}
