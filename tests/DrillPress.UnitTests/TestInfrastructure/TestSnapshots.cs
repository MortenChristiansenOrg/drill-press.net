using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DrillPress.UnitTests.TestInfrastructure;

internal static class TestSnapshots
{
    public static CompilationSnapshot Create(
        string source,
        string sourcePath = "Test.cs",
        bool isGenerated = false) =>
        CompilationSnapshot.Create(CreateProject(sourcePath, source, isGenerated));

    public static ProjectSnapshot CreateProject(
        string sourcePath,
        string source,
        bool isGenerated = false) =>
        new(
            "TestProject",
            "TestProject",
            Path.ChangeExtension(sourcePath, ".csproj"),
            (int)LanguageVersion.CSharp14,
            (int)OutputKind.DynamicallyLinkedLibrary,
            (int)NullableContextOptions.Enable,
            [],
            [new DocumentSnapshot(sourcePath, source, isGenerated)],
            []);
}
