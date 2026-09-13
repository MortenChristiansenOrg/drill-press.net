using Xunit;

namespace DrillPress.IntegrationTests.TestInfrastructure;

public sealed class CompilerFixture : IntegrationTest, IAsyncLifetime
{
    public string Target { get; } = RepositoryPath("fixtures", "CompilerSnapshot", "Selected.slnx");
    public string Consumer { get; } =
        RepositoryPath("fixtures", "CompilerSnapshot", "Consumer", "Consumer.csproj");

    public async ValueTask InitializeAsync()
    {
        foreach (var project in new[] { "Generator", "Interop" })
        {
            var result = await RunProcessAsync(
                "dotnet",
                [
                    "build",
                    RepositoryPath("fixtures", "CompilerSnapshot", project, project + ".csproj"),
                    "-c",
                    "Release",
                ],
                RepositoryRoot,
                TestContext.Current.CancellationToken
            );
            Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        }

        await RestoreAsync(Target);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
