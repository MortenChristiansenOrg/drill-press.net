using System.IO.Compression;
using DrillPress.Release;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.Release;

public sealed class PackageInventoryTests
{
    private readonly ReleasePackageFixture _fixture = new();

    [Fact]
    public void Accepts_exactly_five_matching_packages_in_dependency_order()
    {
        var inventory = _fixture.CreateInventory();

        var packages = inventory.Read(_fixture.Directory, _fixture.Version);

        Assert.Equal(PackageInventory.PackageIds, packages.Select(package => package.Id));
        Assert.Equal(
            Enumerable.Repeat("1.0.0-rc.1", 5),
            packages.Select(package => package.Version)
        );
    }

    [Fact]
    public void Repository_signature_does_not_change_payload_hashes()
    {
        var inventory = _fixture.CreateInventory();
        var original = inventory
            .Read(_fixture.Directory, _fixture.Version)
            .Single(package => package.Id == "DrillPress.Manifest");
        using (var stream = _fixture.FileSystem.File.Open(original.Path, FileMode.Open))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        using (var signature = archive.CreateEntry(".signature.p7s").Open())
        {
            signature.Write([1, 2, 3]);
        }

        var signed = inventory
            .Read(_fixture.Directory, _fixture.Version)
            .Single(package => package.Id == original.Id);

        Assert.Equal(
            original.Contents.OrderBy(entry => entry.Key),
            signed.Contents.OrderBy(entry => entry.Key)
        );
    }

    [Fact]
    public void Rejects_missing_package()
    {
        _fixture.FileSystem.File.Delete(
            _fixture.FileSystem.Path.Combine(_fixture.Directory, "DrillPress.Testing.nupkg")
        );
        var inventory = _fixture.CreateInventory();

        var error = Assert.Throws<InvalidDataException>(() =>
            inventory.Read(_fixture.Directory, _fixture.Version)
        );

        Assert.Equal("Release must contain exactly the five Drill Press packages.", error.Message);
    }

    [Fact]
    public void Rejects_extra_package()
    {
        _fixture.AddPackage("DrillPress.BuildHost");
        var inventory = _fixture.CreateInventory();

        var error = Assert.Throws<InvalidDataException>(() =>
            inventory.Read(_fixture.Directory, _fixture.Version)
        );

        Assert.Equal(
            "Unexpected package DrillPress.BuildHost 1.0.0-rc.1; expected 1.0.0-rc.1.",
            error.Message
        );
    }

    [Fact]
    public void Rejects_mismatched_dependency()
    {
        _fixture.AddPackage("DrillPress.Engine", dependencyVersion: "[1.0.0]");
        var inventory = _fixture.CreateInventory();

        var error = Assert.Throws<InvalidDataException>(() =>
            inventory.Read(_fixture.Directory, _fixture.Version)
        );

        Assert.Equal(
            "DrillPress.Engine must pin DrillPress.Manifest to [1.0.0-rc.1].",
            error.Message
        );
    }

    [Fact]
    public void Rejects_mismatched_package_version()
    {
        _fixture.AddPackage("DrillPress.Engine", version: "1.0.0");
        var inventory = _fixture.CreateInventory();

        var error = Assert.Throws<InvalidDataException>(() =>
            inventory.Read(_fixture.Directory, _fixture.Version)
        );

        Assert.Equal(
            "Unexpected package DrillPress.Engine 1.0.0; expected 1.0.0-rc.1.",
            error.Message
        );
    }

    [Fact]
    public void Rejects_cli_without_buildhost()
    {
        _fixture.AddPackage("DrillPress.Cli", buildHost: false);
        var inventory = _fixture.CreateInventory();

        var error = Assert.Throws<InvalidDataException>(() =>
            inventory.Read(_fixture.Directory, _fixture.Version)
        );

        Assert.Equal("DrillPress.Cli is missing a required assembly.", error.Message);
    }

    [Fact]
    public void Rejects_missing_internal_dependencies()
    {
        _fixture.AddPackage("DrillPress.Engine", dependencies: false);
        var inventory = _fixture.CreateInventory();

        var error = Assert.Throws<InvalidDataException>(() =>
            inventory.Read(_fixture.Directory, _fixture.Version)
        );

        Assert.Equal(
            "DrillPress.Engine has an unexpected internal dependency inventory.",
            error.Message
        );
    }

    [Fact]
    public void Rejects_duplicate_package_ids()
    {
        var path = _fixture.FileSystem.Path.Combine(_fixture.Directory, "DrillPress.Engine.nupkg");
        _fixture.FileSystem.File.Copy(
            path,
            _fixture.FileSystem.Path.Combine(_fixture.Directory, "duplicate.nupkg")
        );
        var inventory = _fixture.CreateInventory();

        var error = Assert.Throws<InvalidDataException>(() =>
            inventory.Read(_fixture.Directory, _fixture.Version)
        );

        Assert.Equal("Release must contain exactly the five Drill Press packages.", error.Message);
    }

    [Fact]
    public void Rejects_stale_assembly_from_another_prerelease()
    {
        _fixture.AssemblyVersion = "1.0.0-rc.0";
        var inventory = _fixture.CreateInventory();

        Assert.Throws<InvalidDataException>(() =>
            inventory.Read(_fixture.Directory, _fixture.Version)
        );
    }
}
