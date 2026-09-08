using System.IO.Abstractions.TestingHelpers;
using DrillPress.Benchmarks;
using DrillPress.Manifest;
using Xunit;

namespace DrillPress.UnitTests.DrillPress.Benchmarks;

public sealed class SignatureNormalizationTests
{
    [Fact]
    public void Different_roots_and_run_ids_preserve_complete_signatures()
    {
        var fileSystem = new MockFileSystem();
        var first = Create(fileSystem.Path.GetFullPath("first"), "a");
        var second = Create(fileSystem.Path.GetFullPath("second"), "b");
        var left = new SignatureNormalization(fileSystem, first.Snapshot, fileSystem.Path.GetFullPath("first"));
        var right = new SignatureNormalization(fileSystem, second.Snapshot, fileSystem.Path.GetFullPath("second"));

        var expected = left.Response(first.Response);
        var actual = right.Response(second.Response);

        Assert.Equal(SignatureNormalization.Hash(expected), SignatureNormalization.Hash(actual));
        Assert.Equal(SignatureNormalization.Hash(left.Snapshot(first.Snapshot)), SignatureNormalization.Hash(right.Snapshot(second.Snapshot)));
        Assert.Equal("literal /first/unchanged", actual.Batches[0].Edits[0].Replacement);
        Assert.Equal("message /first/unchanged", actual.Contexts[0].Findings[0].Message);
    }

    [Fact]
    public void Context_safety_and_batch_associations_remain_observable()
    {
        var fileSystem = new MockFileSystem();
        var input = Create(fileSystem.Path.GetFullPath("first"), "a");
        var changed = input.Response with { Batches = [input.Response.Batches[0] with { Validations = [new("a", false)] }] };
        var left = new SignatureNormalization(fileSystem, input.Snapshot, fileSystem.Path.GetFullPath("first"));
        var right = new SignatureNormalization(fileSystem, input.Snapshot, fileSystem.Path.GetFullPath("first"));

        var expected = left.Response(input.Response);
        var actual = right.Response(changed);

        Assert.NotEqual(SignatureNormalization.Hash(expected), SignatureNormalization.Hash(actual));
        Assert.NotEqual(expected.Contexts[0].Findings[0].BatchId, actual.Contexts[0].Findings[0].BatchId);
        Assert.False(actual.Batches[0].Validations[0].IsSafe);
    }

    [Fact]
    public void Input_signature_preserves_unreported_documents_and_editability()
    {
        var fileSystem = new MockFileSystem();
        var input = Create(fileSystem.Path.GetFullPath("first"), "a");
        var project = input.Snapshot.Projects[0];
        var changed = input.Snapshot with { Projects = [project with { Documents = [project.Documents[0] with { IsEditable = false }] }] };
        var normalization = new SignatureNormalization(fileSystem, input.Snapshot, fileSystem.Path.GetFullPath("first"));

        var expected = normalization.Snapshot(input.Snapshot);
        var actual = normalization.Snapshot(changed);

        Assert.NotEqual(SignatureNormalization.Hash(expected), SignatureNormalization.Hash(actual));
    }

    [Fact]
    public void Generated_document_uris_normalize_only_the_context_identity()
    {
        var fileSystem = new MockFileSystem();
        var first = Create(fileSystem.Path.GetFullPath("first"), "a");
        var second = Create(fileSystem.Path.GetFullPath("second"), "b");
        var left = WithGenerated(first.Snapshot, "a");
        var right = WithGenerated(second.Snapshot, "b");
        var leftNormalization = new SignatureNormalization(fileSystem, left, fileSystem.Path.GetFullPath("first"));
        var rightNormalization = new SignatureNormalization(fileSystem, right, fileSystem.Path.GetFullPath("second"));

        var expected = leftNormalization.Snapshot(left);
        var actual = rightNormalization.Snapshot(right);

        Assert.Equal(SignatureNormalization.Hash(expected), SignatureNormalization.Hash(actual));
        Assert.Equal("generated drillpress-generated://a/source", actual.Projects[0].Documents[1].Text);
    }

    [Fact]
    public void Compiler_option_dictionary_order_does_not_change_the_input_signature()
    {
        var fileSystem = new MockFileSystem();
        var input = Create(fileSystem.Path.GetFullPath("root"), "a");
        var project = input.Snapshot.Projects[0];
        var first = input.Snapshot with { Projects = [project with { CompilerOptions = project.CompilerOptions with
        {
            SpecificDiagnosticOptions = new() { ["CS1702"] = 5, ["CS1701"] = 1 },
        } }] };
        var second = input.Snapshot with { Projects = [project with { CompilerOptions = project.CompilerOptions with
        {
            SpecificDiagnosticOptions = new() { ["CS1701"] = 1, ["CS1702"] = 5 },
        } }] };
        var normalization = new SignatureNormalization(fileSystem, input.Snapshot, fileSystem.Path.GetFullPath("root"));

        var expected = normalization.Snapshot(first);
        var actual = normalization.Snapshot(second);

        Assert.Equal(SignatureNormalization.Hash(expected), SignatureNormalization.Hash(actual));
        Assert.Equal([new KeyValuePair<string, int>("CS1701", 1), new("CS1702", 5)], actual.Projects[0].CompilerOptions.SpecificDiagnosticOptions);
    }

    [Fact]
    public void Rejects_colliding_context_identities_instead_of_hiding_association_differences()
    {
        var fileSystem = new MockFileSystem();
        var root = fileSystem.Path.GetFullPath("root");
        var input = Create(root, "a");
        var project = input.Snapshot.Projects[0];
        var snapshot = input.Snapshot with { Projects = [project, project with
        {
            ContextId = "b", Documents = [project.Documents[0] with { DocumentId = "b-doc" }],
        }] };

        Assert.Throws<InvalidDataException>(() => new SignatureNormalization(fileSystem, snapshot, root));
    }

    private static CompilationSnapshot WithGenerated(CompilationSnapshot snapshot, string id)
    {
        var path = "drillpress-generated://" + id + "/000001/Generator.g.cs";
        var generated = new DocumentSnapshot(path, "generated drillpress-generated://a/source", true) { DocumentId = id + "-generated" };
        return snapshot with { Projects = [snapshot.Projects[0] with { Documents = [.. snapshot.Projects[0].Documents, generated] }] };
    }

    private static (CompilationSnapshot Snapshot, BundleResponse Response) Create(string root, string id)
    {
        var path = System.IO.Path.Combine(root, "Shared.cs");
        var document = new DocumentSnapshot(path, "alpha", false) { DocumentId = id + "-doc", FileIdentity = path, IsEditable = true, Fingerprint = "hash" };
        var project = new ProjectSnapshot("P", "P", System.IO.Path.Combine(root, "P.csproj"), 14, 2, 0, [], [document], []) { ContextId = id };
        var snapshot = CompilationSnapshot.Create(project) with { RequestId = id };
        var response = new BundleResponse(1, id, [new(id, true, [new("R", "message /first/unchanged", document.DocumentId, 0, 5, id + "-batch")])],
            [new(id + "-batch", [new(path, "hash", 0, 5, "alpha", "literal /first/unchanged")], [new(id, true)])]);
        return (snapshot, response);
    }
}
