using System.IO.Abstractions.TestingHelpers;
using DrillPress.Engine;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DrillPress.UnitTests.Engine;

public sealed class AnalysisEngineTests
{
    [Fact]
    public void Public_construction_requires_no_external_dependencies()
    {
        var type = typeof(AnalysisEngine);

        var constructors = type.GetConstructors();

        Assert.Equal([0], constructors.Select(constructor => constructor.GetParameters().Length));
    }

    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public async Task Binds_metadata_assemblies_that_exist_only_in_the_injected_filesystem()
    {
        const string metadataSource = """
            namespace System
            {
                public class Object { }
                public class ValueType { }
                public class Enum : ValueType { }
                public struct Void { }
                public struct Int32 { }
                public struct Boolean { }
                public class Attribute { }
                public enum AttributeTargets { All = 32767 }
                public class AttributeUsageAttribute : Attribute
                {
                    public AttributeUsageAttribute(AttributeTargets targets) { }
                    public bool AllowMultiple { get; set; }
                    public bool Inherited { get; set; }
                }
            }
            namespace Sample { public sealed class Target { public static Target Empty => null; } }
            """;
        var compilation = CSharpCompilation.Create("Dependency",
            [CSharpSyntaxTree.ParseText(metadataSource, cancellationToken: TestContext.Current.CancellationToken)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emission = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        var assemblyPath = _fileSystem.Path.GetFullPath("Dependency.dll");
        _fileSystem.AddFile(assemblyPath, new MockFileData(image.ToArray()));
        const string source = "class Values { Sample.Target Value => Sample.Target.Empty; }";
        var project = TestSnapshots.CreateProject("Values.cs", source) with { MetadataReferences = [assemblyPath] };
        var snapshot = CompilationSnapshot.Create(project);

        var diagnostics = await new AnalysisEngine(_fileSystem).AnalyzeAsync(
            RuleTestData.TargetEmptyRuleSet(), snapshot, TestContext.Current.CancellationToken);

        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TEST001", diagnostic.Descriptor.Id);
        Assert.Equal(new SourceLocation("Values.cs", 38, 19, 1, 39), diagnostic.Location);
    }

    [Fact]
    public async Task Finds_only_the_matching_member_reference_at_its_physical_location()
    {
        const string source = """
            namespace Sample;

            public sealed class Target
            {
                public static Target Empty => null;
            }

            public static class Values
            {
                public static Target Violation => Target.Empty;
                public static Target Compliant => null;
            }
            """;
        var snapshot = TestSnapshots.Create(source, "Values.cs");

        var diagnostics = await new AnalysisEngine(_fileSystem).AnalyzeAsync(
            RuleTestData.TargetEmptyRuleSet(),
            snapshot,
            TestContext.Current.CancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TEST001", diagnostic.Descriptor.Id);
        Assert.Equal("Values.cs", diagnostic.Location.FilePath);
        Assert.Equal(10, diagnostic.Location.Line);
        Assert.Equal(39, diagnostic.Location.Column);
        Assert.Equal("Target.Empty", source.Substring(diagnostic.Location.Start, diagnostic.Location.Length));
    }

    [Fact]
    public async Task Ignores_matching_references_in_generated_documents()
    {
        var snapshot = TestSnapshots.Create(
            """
            namespace Sample;
            public sealed class Target
            {
                public static Target Empty => null;
            }
            public static class Generated
            {
                public static Target Value => Target.Empty;
            }
            """,
            "Generated.g.cs",
            isGenerated: true);

        var diagnostics = await new AnalysisEngine(_fileSystem).AnalyzeAsync(
            RuleTestData.TargetEmptyRuleSet(),
            snapshot,
            TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }
    [Fact]
    public async Task Conditional_linked_source_is_evaluated_independently_in_every_context()
    {
        const string source = """
            namespace Sample;
            public class Target { public static Target Empty => null; }
            public class Values
            {
            #if INCLUDED
                public Target Value => Target.Empty;
            #endif
            }
            """;
        var first = TestSnapshots.CreateProject("Shared.cs", source) with { ContextId = "first", PreprocessorSymbols = ["INCLUDED"] };
        var second = TestSnapshots.CreateProject("Shared.cs", source) with { ContextId = "second", PreprocessorSymbols = [] };
        var snapshot = CompilationSnapshot.Create(first, second);

        var response = await new AnalysisEngine(_fileSystem).EvaluateAsync(
            RuleTestData.TargetEmptyRuleSet(), snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(["first", "second"], response.Contexts.Select(context => context.ContextId));
        Assert.Equal([true, true], response.Contexts.Select(context => context.IsComplete));
        Assert.Equal([1, 0], response.Contexts.Select(context => context.Findings.Length));
        Assert.Equal(first.Documents[0].DocumentId, response.Contexts[0].Findings[0].DocumentId);
        Assert.Equal(snapshot.RequestId, response.RequestId);
    }

    [Fact]
    public void Changed_external_reference_is_rejected_before_metadata_loading()
    {
        var path = _fileSystem.Path.GetFullPath("Changed.dll");
        _fileSystem.AddFile(path, new MockFileData("changed"));
        var project = TestSnapshots.CreateProject("Source.cs", "class Source { }") with
        {
            ExternalReferences = [new MetadataReferenceSnapshot(path, new string('0', 64), [], false, 0)],
        };

        var error = Assert.Throws<InvalidDataException>(() => new AnalysisEngine(_fileSystem).Reconstruct(CompilationSnapshot.Create(project), TestContext.Current.CancellationToken));

        Assert.Equal($"External reference changed: '{path}'. Export the target again.", error.Message);
    }

    [Fact]
    public void Missing_external_reference_is_not_silently_dropped()
    {
        var path = _fileSystem.Path.GetFullPath("Missing.dll");
        var project = TestSnapshots.CreateProject("Source.cs", "class Source { }") with
        {
            ExternalReferences = [new MetadataReferenceSnapshot(path, new string('0', 64), [], false, 0)],
        };

        var error = Assert.Throws<FileNotFoundException>(() => new AnalysisEngine(_fileSystem).Reconstruct(CompilationSnapshot.Create(project), TestContext.Current.CancellationToken));

        Assert.Equal(path, error.FileName);
    }

    [Fact]
    public async Task Ambiguous_binding_does_not_substitute_a_candidate_symbol()
    {
        var snapshot = TestSnapshots.Create("""
            namespace Sample;
            class A { }
            class B { }
            class Target
            {
                public static Target Empty(A value) => null;
                public static Target Empty(B value) => null;
                public Target Value => Target.Empty(null);
            }
            """);

        var diagnostics = await new AnalysisEngine(_fileSystem).AnalyzeAsync(RuleTestData.TargetEmptyRuleSet(), snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Source_reference_cycles_are_rejected()
    {
        var project = TestSnapshots.CreateProject("Source.cs", "class Source { }") with
        {
            ContextId = "self", ReferencedContextIds = ["self"],
            CompilationReferences = [new CompilationReferenceSnapshot("self", [], false)],
        };

        var error = Assert.Throws<InvalidDataException>(() => new AnalysisEngine(_fileSystem).Reconstruct(CompilationSnapshot.Create(project), TestContext.Current.CancellationToken));

        Assert.Equal("The source compilation graph contains a cycle.", error.Message);
    }

    [Fact]
    public async Task Anonymous_type_members_do_not_require_a_metadata_type_name()
    {
        var snapshot = TestSnapshots.Create("class Values { object Value => new { Name = \"value\" }.Name; }");

        var diagnostics = await new AnalysisEngine(_fileSystem).AnalyzeAsync(RuleTestData.TargetEmptyRuleSet(), snapshot, TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

}
