using DrillPress.Benchmarks;
using DrillPress.UnitTests.TestInfrastructure;
using Xunit;

namespace DrillPress.UnitTests.DrillPress.Benchmarks;

public sealed class RepositoryBenchmarkTests
{
    [Fact]
    public async Task Failed_report_preserves_the_repository_preparation_failure()
    {
        var fileSystem = new ReportFailureFileSystem();
        var blockedParent = fileSystem.Path.GetFullPath("blocked");
        fileSystem.AddFile(blockedParent, new("existing file"));
        var checkout = fileSystem.Path.Combine(blockedParent, "xunit");
        var output = fileSystem.Path.GetFullPath("report-output");
        var benchmark = new RepositoryBenchmark(fileSystem);

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            benchmark.RunAsync(checkout, output, 1, TestContext.Current.CancellationToken)
        );

        Assert.Collection(
            exception.InnerExceptions,
            original =>
                Assert.Equal(
                    "The file 'path' already exists.",
                    Assert.IsType<IOException>(original).Message
                ),
            report => Assert.Same(fileSystem.ReportFailure, report)
        );
        Assert.Equal("existing file", fileSystem.File.ReadAllText(blockedParent));
        Assert.True(fileSystem.File.Exists(fileSystem.Path.Combine(output, "preparation.json")));
    }
}
