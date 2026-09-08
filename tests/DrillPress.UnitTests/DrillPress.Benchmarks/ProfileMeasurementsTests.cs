using DrillPress.Manifest;
using System.IO.Abstractions.TestingHelpers;
using DrillPress.Benchmarks;
using Xunit;

namespace DrillPress.UnitTests.DrillPress.Benchmarks;

public sealed class ProfileMeasurementsTests
{
    [Fact]
    public void Preserves_complete_events_alongside_nonprofile_stderr()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("profile", new("""
            workspace warning
            drillpress-profile {"component":"rules","phase":"total","processId":1,"wallMilliseconds":12,"userCpuMilliseconds":8,"systemCpuMilliseconds":2,"processPeakWorkingSetBytes":1024,"count":null}
            """));
        var measurements = new ProfileMeasurements(fileSystem);

        var events = measurements.Read("profile", "rules");

        Assert.Equal([new ProfileEvent("rules", "total", 1, 12, 8, 2, 1024, null)], events);
    }

    [Theory]
    [InlineData("warning only")]
    [InlineData("drillpress-profile {\"component\":\"rules\",\"phase\":\"total\",\"processId\":1,\"wallMilliseconds\":-1,\"userCpuMilliseconds\":0,\"systemCpuMilliseconds\":0,\"processPeakWorkingSetBytes\":1,\"count\":null}")]
    public void Rejects_missing_or_invalid_measurements(string output)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("profile", new(output));
        var measurements = new ProfileMeasurements(fileSystem);

        Assert.Throws<InvalidDataException>(() => measurements.Read("profile", "rules"));
    }
}
