using System.IO.Abstractions.TestingHelpers;
using DrillPress.Manifest;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Manifest;

public sealed class CompilationSnapshotFileTests
{
    [Fact]
    public void Public_construction_requires_no_external_dependencies()
    {
        var type = typeof(CompilationSnapshotFile);

        var constructors = type.GetConstructors();

        Assert.Equal([0], constructors.Select(constructor => constructor.GetParameters().Length));
    }

    private readonly MockFileSystem _fileSystem = new();
    private const string SnapshotPath = "snapshot.json";

    [Fact]
    public async Task Writes_the_complete_envelope_using_the_injected_filesystem()
    {
        var storage = new CompilationSnapshotFile(_fileSystem);
        var snapshot = (CompilationSnapshot.Create() with { RequestId = "request" });

        await storage.WriteAsync(SnapshotPath, snapshot, TestContext.Current.CancellationToken);

        Assert.Equal(
            """{"fileIdentifier":"drillpress-compilation","formatVersion":2,"projects":[],"requestId":"request"}""",
            _fileSystem.File.ReadAllText(SnapshotPath)
        );
    }

    [Fact]
    public async Task Replaces_an_existing_snapshot_without_leaving_temporary_files()
    {
        _fileSystem.AddFile(SnapshotPath, new MockFileData("previous snapshot"));
        var storage = new CompilationSnapshotFile(_fileSystem);

        await storage.WriteAsync(
            SnapshotPath,
            (CompilationSnapshot.Create() with { RequestId = "request" }),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            """{"fileIdentifier":"drillpress-compilation","formatVersion":2,"projects":[],"requestId":"request"}""",
            _fileSystem.File.ReadAllText(SnapshotPath)
        );
        Assert.Equal([_fileSystem.Path.GetFullPath(SnapshotPath)], _fileSystem.AllFiles);
    }

    [Fact]
    public async Task Already_cancelled_write_preserves_the_existing_snapshot()
    {
        _fileSystem.AddFile(SnapshotPath, new MockFileData("previous snapshot"));
        var storage = new CompilationSnapshotFile(_fileSystem);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.WriteAsync(
                SnapshotPath,
                (CompilationSnapshot.Create() with { RequestId = "request" }),
                cancellation.Token
            )
        );

        Assert.Equal("previous snapshot", _fileSystem.File.ReadAllText(SnapshotPath));
        Assert.Equal([_fileSystem.Path.GetFullPath(SnapshotPath)], _fileSystem.AllFiles);
    }

    [Fact]
    public async Task Failed_write_preserves_the_existing_snapshot_and_removes_the_partial_file()
    {
        var failure = new IOException("Simulated write failure.");
        var fileSystem = new SnapshotWriteFailureFileSystem(failure);
        fileSystem.AddFile(SnapshotPath, new MockFileData("previous snapshot"));
        var storage = new CompilationSnapshotFile(fileSystem);

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            storage.WriteAsync(
                SnapshotPath,
                (CompilationSnapshot.Create() with { RequestId = "request" }),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Same(failure, exception);
        Assert.Equal("previous snapshot", fileSystem.File.ReadAllText(SnapshotPath));
        Assert.Equal([fileSystem.Path.GetFullPath(SnapshotPath)], fileSystem.AllFiles);
    }

    [Fact]
    public async Task Cancelled_write_preserves_the_existing_snapshot_and_removes_the_partial_file()
    {
        var failure = new OperationCanceledException();
        var fileSystem = new SnapshotWriteFailureFileSystem(failure);
        fileSystem.AddFile(SnapshotPath, new MockFileData("previous snapshot"));
        var storage = new CompilationSnapshotFile(fileSystem);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            storage.WriteAsync(
                SnapshotPath,
                (CompilationSnapshot.Create() with { RequestId = "request" }),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Same(failure, exception);
        Assert.Equal("previous snapshot", fileSystem.File.ReadAllText(SnapshotPath));
        Assert.Equal([fileSystem.Path.GetFullPath(SnapshotPath)], fileSystem.AllFiles);
    }

    [Fact]
    public async Task Null_json_reports_the_requested_snapshot_path()
    {
        _fileSystem.AddFile(SnapshotPath, new MockFileData("null"));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CompilationSnapshotFile(_fileSystem).ReadAsync(
                SnapshotPath,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal("Compilation snapshot 'snapshot.json' is empty.", error.Message);
    }

    [Fact]
    public async Task Round_trips_the_current_snapshot_format_in_memory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expectedProject = TestSnapshots.CreateProject(
            "Example.cs",
            "public class Example { }"
        ) with
        {
            PreprocessorSymbols = ["FEATURE"],
            ProjectReferences = [new MetadataImageSnapshot([1, 2, 3], ["Dependency"], false)],
        };
        var expected = CompilationSnapshot.Create(expectedProject);
        var storage = new CompilationSnapshotFile(_fileSystem);
        await storage.WriteAsync(SnapshotPath, expected, cancellationToken);

        var snapshot = await storage.ReadAsync(SnapshotPath, cancellationToken);

        Assert.Equal(CompilationSnapshot.ExpectedFileIdentifier, snapshot.FileIdentifier);
        Assert.Equal(CompilationSnapshot.CurrentFormatVersion, snapshot.FormatVersion);
        var project = Assert.Single(snapshot.Projects);
        Assert.Equal(expectedProject.Name, project.Name);
        Assert.Equal(expectedProject.AssemblyName, project.AssemblyName);
        Assert.Equal(expectedProject.ProjectPath, project.ProjectPath);
        Assert.Equal(expectedProject.LanguageVersion, project.LanguageVersion);
        Assert.Equal(expectedProject.OutputKind, project.OutputKind);
        Assert.Equal(expectedProject.NullableContextOptions, project.NullableContextOptions);
        Assert.Equal(expectedProject.PreprocessorSymbols, project.PreprocessorSymbols);
        Assert.Equal(expectedProject.MetadataReferences, project.MetadataReferences);
        Assert.Equal(expectedProject.Documents, project.Documents);
        var reference = Assert.Single(project.ProjectReferences);
        Assert.Equal(expectedProject.ProjectReferences[0].Image, reference.Image);
        Assert.Equal(expectedProject.ProjectReferences[0].Aliases, reference.Aliases);
        Assert.Equal(
            expectedProject.ProjectReferences[0].EmbedInteropTypes,
            reference.EmbedInteropTypes
        );
    }

    [Theory]
    [InlineData(
        """{"fileIdentifier":"drillpress-compilation","formatVersion":2,"requestId":"request"}"""
    )]
    [InlineData(
        """{"fileIdentifier":"drillpress-compilation","formatVersion":2,"projects":null,"requestId":"request"}"""
    )]
    public async Task Read_rejects_missing_or_null_projects(string json)
    {
        _fileSystem.AddFile(SnapshotPath, new MockFileData(json));

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            new CompilationSnapshotFile(_fileSystem).ReadAsync(
                SnapshotPath,
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task Write_rejects_an_unknown_file_identifier_in_memory()
    {
        var snapshot = new CompilationSnapshot("unknown", 1, []);
        _fileSystem.AddFile(SnapshotPath, new MockFileData("untouched"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CompilationSnapshotFile(_fileSystem).WriteAsync(
                SnapshotPath,
                snapshot,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal("The input is not a Drill Press compilation snapshot.", exception.Message);
        Assert.Equal("untouched", _fileSystem.File.ReadAllText(SnapshotPath));
    }

    [Fact]
    public async Task Read_rejects_an_unsupported_format_version_in_memory()
    {
        _fileSystem.AddFile(
            SnapshotPath,
            new MockFileData(
                """
                {"fileIdentifier":"drillpress-compilation","formatVersion":99,"projects":[]}
                """
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CompilationSnapshotFile(_fileSystem).ReadAsync(
                SnapshotPath,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(
            "Compilation snapshot format 99 is not supported; expected 2. Use matching Drill Press components.",
            exception.Message
        );
    }

    [Fact]
    public async Task Incompatible_header_is_rejected_before_deserializing_payload_members()
    {
        _fileSystem.AddFile(
            SnapshotPath,
            new MockFileData(
                """{"fileIdentifier":"drillpress-compilation","formatVersion":-1,"projects":"not a project array"}"""
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CompilationSnapshotFile(_fileSystem).ReadAsync(
                SnapshotPath,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(
            "Compilation snapshot format -1 is not supported; expected 2. Use matching Drill Press components.",
            exception.Message
        );
    }

    [Fact]
    public async Task Source_identity_and_context_graph_round_trip_with_response_association()
    {
        var fixture = new ContractFixture();
        var storage = new CompilationSnapshotFile(_fileSystem);
        await storage.WriteAsync(
            SnapshotPath,
            fixture.Snapshot,
            TestContext.Current.CancellationToken
        );

        var snapshot = await storage.ReadAsync(SnapshotPath, TestContext.Current.CancellationToken);
        var result = new BundleResponseValidator().Validate(snapshot, fixture.Response);

        Assert.Equal("request", snapshot.RequestId);
        Assert.Equal(["first", "second"], snapshot.Projects.Select(project => project.ContextId));
        Assert.Equal(["second"], snapshot.Projects[0].ReferencedContextIds);
        Assert.Equal(
            ["net10.0", "net9.0"],
            snapshot.Projects.Select(project => project.TargetFramework)
        );
        Assert.Equal(fixture.Document, snapshot.Projects[0].Documents[0]);
        Assert.Equal([fixture.Edit(2, 5, "alpha", "A")], result.Edits);
    }

    [Fact]
    public async Task Reordered_header_still_reports_incompatibility_before_payload_deserialization()
    {
        _fileSystem.AddFile(
            SnapshotPath,
            new MockFileData(
                """{"projects":"not a project array","formatVersion":-1,"fileIdentifier":"drillpress-compilation"}"""
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CompilationSnapshotFile(_fileSystem).ReadAsync(
                SnapshotPath,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(
            "Compilation snapshot format -1 is not supported; expected 2. Use matching Drill Press components.",
            exception.Message
        );
    }

    [Theory]
    [InlineData("compilationReferences")]
    [InlineData("externalReferences")]
    public async Task Null_reference_collections_are_rejected_during_deserialization(
        string property
    )
    {
        var snapshot = CompilationSnapshot.Create(
            TestSnapshots.CreateProject("Source.cs", "class Source { }")
        );
        var storage = new CompilationSnapshotFile(_fileSystem);
        await storage.WriteAsync(SnapshotPath, snapshot, TestContext.Current.CancellationToken);
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            _fileSystem.File.ReadAllText(SnapshotPath)
        )!;
        json["projects"]![0]![property] = null;
        _fileSystem.File.WriteAllText(SnapshotPath, json.ToJsonString());

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            storage.ReadAsync(SnapshotPath, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Null_metadata_fingerprint_is_rejected_during_deserialization()
    {
        var project = TestSnapshots.CreateProject("Source.cs", "class Source { }") with
        {
            ExternalReferences =
            [
                new MetadataReferenceSnapshot("Reference.dll", new string('0', 64), [], false, 0),
            ],
        };
        var storage = new CompilationSnapshotFile(_fileSystem);
        await storage.WriteAsync(
            SnapshotPath,
            CompilationSnapshot.Create(project),
            TestContext.Current.CancellationToken
        );
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            _fileSystem.File.ReadAllText(SnapshotPath)
        )!;
        json["projects"]![0]!["externalReferences"]![0]!["fingerprint"] = null;
        _fileSystem.File.WriteAllText(SnapshotPath, json.ToJsonString());

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            storage.ReadAsync(SnapshotPath, TestContext.Current.CancellationToken)
        );
    }
}
