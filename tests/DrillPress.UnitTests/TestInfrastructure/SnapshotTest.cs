using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.UnitTests.TestInfrastructure;

public abstract class SnapshotTest : IDisposable
{
    private readonly List<string> _temporaryPaths = [];

    public void Dispose()
    {
        foreach (var temporaryPath in _temporaryPaths)
        {
            File.Delete(temporaryPath);
        }

        GC.SuppressFinalize(this);
    }

    protected async Task<string> WriteSnapshotAsync(
        string sourcePath,
        string source,
        CancellationToken cancellationToken,
        bool isGenerated = false)
    {
        var snapshot = CompilationSnapshot.Create(
            TestSnapshots.CreateProject(sourcePath, source, isGenerated));

        return await WriteSnapshotAsync(snapshot, cancellationToken);
    }

    protected async Task<string> WriteSnapshotAsync(
        CompilationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var snapshotPath = CreateTemporaryPath(".drillpress.json");
        await snapshot.WriteAsync(snapshotPath, cancellationToken);
        return snapshotPath;
    }

    protected string CreateTemporaryPath(string extension = ".tmp")
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        _temporaryPaths.Add(path);
        return path;
    }
}

internal static class TestSnapshots
{
    public static ProjectSnapshot CreateProject(
        string sourcePath,
        string source,
        bool isGenerated = false)
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("The runtime did not expose its trusted platform assemblies.");
        var references = trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path =>
                Path.GetFileName(path) is "System.Private.CoreLib.dll" or "System.Runtime.dll" or "netstandard.dll")
            .ToArray();

        return new ProjectSnapshot(
            "TestProject",
            "TestProject",
            Path.ChangeExtension(sourcePath, ".csproj"),
            (int)LanguageVersion.CSharp14,
            (int)OutputKind.DynamicallyLinkedLibrary,
            (int)NullableContextOptions.Enable,
            [],
            [new DocumentSnapshot(sourcePath, source, isGenerated)],
            references);
    }
}
