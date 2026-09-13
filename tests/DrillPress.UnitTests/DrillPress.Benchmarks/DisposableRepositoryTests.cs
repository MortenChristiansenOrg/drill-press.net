using System.IO.Abstractions.TestingHelpers;
using DrillPress.Benchmarks;
using Xunit;

namespace DrillPress.UnitTests.DrillPress.Benchmarks;

public sealed class DisposableRepositoryTests
{
    [Fact]
    public void Copies_metadata_and_source_without_sharing_mutable_files()
    {
        var fileSystem = new MockFileSystem();
        var root = fileSystem.Path.GetFullPath("checkout");
        fileSystem.AddFile(fileSystem.Path.Combine(root, ".git", "HEAD"), new("revision"));
        fileSystem.AddFile(fileSystem.Path.Combine(root, "src", "Input.cs"), new("original"));
        using var copy = new DisposableRepository(fileSystem, root, CancellationToken.None);

        fileSystem.File.WriteAllText(
            fileSystem.Path.Combine(copy.Root, "src", "Input.cs"),
            "changed"
        );

        Assert.Equal(
            "original",
            fileSystem.File.ReadAllText(fileSystem.Path.Combine(root, "src", "Input.cs"))
        );
        Assert.Equal(
            "changed",
            fileSystem.File.ReadAllText(fileSystem.Path.Combine(copy.Root, "src", "Input.cs"))
        );
        Assert.Equal(
            "revision",
            fileSystem.File.ReadAllText(fileSystem.Path.Combine(copy.Root, ".git", "HEAD"))
        );
    }

    [Fact]
    public void Removes_copied_readonly_git_objects_without_changing_the_checkout()
    {
        var fileSystem = new MockFileSystem();
        var root = fileSystem.Path.GetFullPath("checkout");
        var gitObject = fileSystem.Path.Combine(root, ".git", "objects", "pack");
        fileSystem.AddFile(gitObject, new("packed") { Attributes = FileAttributes.ReadOnly });
        var copy = new DisposableRepository(fileSystem, root, CancellationToken.None);
        var copiedRoot = copy.Root;

        copy.Dispose();

        Assert.False(fileSystem.Directory.Exists(copiedRoot));
        Assert.Equal("packed", fileSystem.File.ReadAllText(gitObject));
        Assert.Equal(FileAttributes.ReadOnly, fileSystem.File.GetAttributes(gitObject));
    }

    [Fact]
    public void Repeated_disposal_leaves_the_original_repository_available()
    {
        var fileSystem = new MockFileSystem();
        var root = fileSystem.Path.GetFullPath("checkout");
        fileSystem.AddFile(fileSystem.Path.Combine(root, "Input.cs"), new("original"));
        var copy = new DisposableRepository(fileSystem, root, CancellationToken.None);

        copy.Dispose();
        copy.Dispose();

        Assert.False(fileSystem.Directory.Exists(copy.Root));
        Assert.Equal(
            "original",
            fileSystem.File.ReadAllText(fileSystem.Path.Combine(root, "Input.cs"))
        );
    }
}
