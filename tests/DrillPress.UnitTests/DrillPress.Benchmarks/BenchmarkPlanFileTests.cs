using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using DrillPress.Benchmarks;
using DrillPress.BundleVerification;
using Xunit;

namespace DrillPress.UnitTests.DrillPress.Benchmarks;

public sealed class BenchmarkPlanFileTests
{
    private const string SerializedPlan = """
        {"RepositoryRoot":"repo","ManagedBundle":"managed.dll","NativeBundle":"native","Cases":[{"Name":"probe","Arguments":["check","snapshot"],"Outcome":1,"StandardOutput":"AP8=","StandardError":"DQo="}]}
        """;

    private readonly MockFileSystem _fileSystem = new();
    private readonly BenchmarkPlanFile _storage;
    private readonly string _path;

    public BenchmarkPlanFileTests()
    {
        _path = _fileSystem.Path.GetFullPath("plan.json");
        _storage = new BenchmarkPlanFile(_fileSystem);
    }

    [Fact]
    public async Task Writes_the_complete_plan_and_binary_contract()
    {
        var plan = new BenchmarkPlan(
            "repo",
            "managed.dll",
            "native",
            [
                new BundleCase(
                    "probe",
                    ["check", "snapshot"],
                    BundleOutcome.Findings,
                    [0, 255],
                    [13, 10]
                ),
            ]
        );

        await _storage.WriteAsync(_path, plan, TestContext.Current.CancellationToken);

        Assert.Equal(SerializedPlan, _fileSystem.File.ReadAllText(_path));
        Assert.Equal([_path], _fileSystem.AllFiles);
    }

    [Fact]
    public void Reads_the_complete_plan_and_binary_contract()
    {
        _fileSystem.AddFile(_path, new MockFileData(SerializedPlan));

        var plan = _storage.Read(_path);

        Assert.Equal("repo", plan.RepositoryRoot);
        Assert.Equal("managed.dll", plan.ManagedBundle);
        Assert.Equal("native", plan.NativeBundle);
        var scenario = Assert.Single(plan.Cases);
        Assert.Equal("probe", scenario.Name);
        Assert.Equal(["check", "snapshot"], scenario.Arguments);
        Assert.Equal(BundleOutcome.Findings, scenario.Outcome);
        Assert.Equal(new byte[] { 0, 255 }, scenario.StandardOutput);
        Assert.Equal(new byte[] { 13, 10 }, scenario.StandardError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    public void Rejects_malformed_json(string content)
    {
        _fileSystem.AddFile(_path, new MockFileData(content));

        Assert.Throws<JsonException>(() => _storage.Read(_path));
    }

    [Fact]
    public void Rejects_a_null_plan()
    {
        _fileSystem.AddFile(_path, new MockFileData("null"));

        var exception = Assert.Throws<InvalidOperationException>(() => _storage.Read(_path));

        Assert.Equal("The benchmark plan is empty.", exception.Message);
    }

    [Fact]
    public void Rejects_a_missing_plan_file()
    {
        Assert.Throws<FileNotFoundException>(() => _storage.Read(_path));
    }
}
